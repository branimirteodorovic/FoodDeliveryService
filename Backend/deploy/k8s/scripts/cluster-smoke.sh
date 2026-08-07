#!/usr/bin/env bash
#
# Checks that a deployed cluster is actually working. Run it after kind-up.sh.
#
#   1. All nine Deployments report Ready.
#   2. The Gateway answers on the published port, and proxies to a service (the anonymous
#      users/register route reaches Users rather than 404-ing at the Gateway).
#   3. Identity serves its discovery document on its published port.
#   4. Kill a dependency — scale Redis to zero — and the probe split behaves: /health/ready goes
#      503, /health/live stays 200, the pod leaves the Service endpoints and is NOT restarted.
#      Restarting a pod does not bring Redis back; it just adds a crash-loop to the incident.
#   5. Restore Redis; readiness recovers on its own.
#
# Usage: ./cluster-smoke.sh
set -euo pipefail

NAMESPACE="fooddeliveryservice"
REDIS="statefulset/fooddeliveryservice-redis"
GATEWAY_URL="${GATEWAY_URL:-http://localhost:8000}"
IDENTITY_URL="${IDENTITY_URL:-http://localhost:18080}"
LOCAL_PORT="${LOCAL_PORT:-58200}"

DEPLOYMENTS=(
  fooddeliveryservice-identity
  fooddeliveryservice-users-api
  fooddeliveryservice-orders-api
  fooddeliveryservice-restaurants-api
  fooddeliveryservice-delivery-api
  fooddeliveryservice-notifications-api
  fooddeliveryservice-realtime-api
  fooddeliveryservice-frauddetection-api
  fooddeliveryservice-gateway
)

port_forward_pid=""

cleanup() {
  if [ -n "$port_forward_pid" ]; then
    kill "$port_forward_pid" 2>/dev/null || true
  fi
  # Leave the cluster as it was found, even on failure — otherwise a failed run leaves Redis at
  # zero and every later run fails for the wrong reason.
  kubectl -n "$NAMESPACE" scale "$REDIS" --replicas=1 >/dev/null 2>&1 || true
}
trap cleanup EXIT

status_of() {  # $1 = full URL
  curl -s -o /dev/null -w '%{http_code}' --max-time 10 "$1" || echo "000"
}

assert_status() {  # $1 = URL, $2..$n = acceptable codes
  local url="$1"; shift
  local actual
  actual="$(status_of "$url")"
  for expected in "$@"; do
    if [ "$actual" = "$expected" ]; then
      echo "    $url -> $actual"
      return 0
    fi
  done
  echo "FAIL: $url returned $actual, expected one of: $*" >&2
  return 1
}

await_status() {  # $1 = URL, $2 = code, $3 = timeout seconds
  local url="$1" expected="$2" timeout="$3" deadline actual
  deadline=$((SECONDS + timeout))
  while [ $SECONDS -lt $deadline ]; do
    actual="$(status_of "$url")"
    if [ "$actual" = "$expected" ]; then
      echo "    $url -> $actual"
      return 0
    fi
    sleep 3
  done
  echo "FAIL: $url did not reach $expected within ${timeout}s (last: ${actual:-none})" >&2
  return 1
}

restart_count() {
  kubectl -n "$NAMESPACE" get pods -l app.kubernetes.io/name=fooddeliveryservice-orders-api \
    -o jsonpath='{.items[0].status.containerStatuses[0].restartCount}'
}

echo "==> 1. every service is Ready"
for deployment in "${DEPLOYMENTS[@]}"; do
  kubectl -n "$NAMESPACE" rollout status "deployment/$deployment" --timeout=8m >/dev/null
  echo "    $deployment"
done

echo "==> 2. the Gateway is reachable and proxies downstream"
assert_status "$GATEWAY_URL/health/ready" 200
# An empty body on the anonymous registration route: 400 means YARP forwarded it and Users rejected
# the payload, which is the proof that the route, the cluster and the Service DNS all resolve.
# A 404 here would mean the route never matched — the failure mode the Gateway's config had.
assert_status "$GATEWAY_URL/users/register" 400 415 422

echo "==> 3. Identity serves discovery"
assert_status "$IDENTITY_URL/.well-known/openid-configuration" 200

echo "==> 4. break a readiness dependency (Redis -> 0 replicas)"
kubectl -n "$NAMESPACE" port-forward "svc/fooddeliveryservice-orders-api" "$LOCAL_PORT:8080" >/dev/null 2>&1 &
port_forward_pid=$!
sleep 3

assert_status "http://127.0.0.1:$LOCAL_PORT/health/ready" 200
restarts_before="$(restart_count)"

kubectl -n "$NAMESPACE" scale "$REDIS" --replicas=0
kubectl -n "$NAMESPACE" wait --for=delete pod/fooddeliveryservice-redis-0 --timeout=2m

# Readiness must go 503 — the multiplexer reconnects with abortConnect=false, so give it a moment.
await_status "http://127.0.0.1:$LOCAL_PORT/health/ready" 503 120
# …while liveness is unmoved. This is the assertion the whole probe contract turns on.
assert_status "http://127.0.0.1:$LOCAL_PORT/health/live" 200

ready_addresses="$(kubectl -n "$NAMESPACE" get endpoints fooddeliveryservice-orders-api \
  -o jsonpath='{.subsets[*].addresses[*].ip}')"
if [ -n "$ready_addresses" ]; then
  echo "FAIL: pod is still a ready Service endpoint ($ready_addresses) despite /health/ready 503" >&2
  exit 1
fi
echo "    pod removed from the Service endpoints"

restarts_during="$(restart_count)"
if [ "$restarts_during" != "$restarts_before" ]; then
  echo "FAIL: the pod restarted ($restarts_before -> $restarts_during) — liveness is not dependency-independent" >&2
  exit 1
fi
echo "    restartCount unchanged at $restarts_during"

echo "==> 5. restore Redis; readiness recovers by itself"
kubectl -n "$NAMESPACE" scale "$REDIS" --replicas=1
kubectl -n "$NAMESPACE" rollout status "$REDIS" --timeout=3m
await_status "http://127.0.0.1:$LOCAL_PORT/health/ready" 200 120

if [ "$(restart_count)" != "$restarts_before" ]; then
  echo "FAIL: the pod restarted during the outage" >&2
  exit 1
fi

echo
echo "cluster smoke passed"

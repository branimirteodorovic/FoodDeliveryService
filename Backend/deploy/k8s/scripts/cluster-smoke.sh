#!/usr/bin/env bash
#
# Checks that a deployed cluster is actually working. Run it after kind-up.sh.
#
#   1. All eight Deployments report Ready.
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
  fooddeliveryservice-support-api
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

# The same, for a route that only answers a POST. Without this the register check below GETs a
# `MapPost` endpoint and reads the resulting 405 as a failure — which is a bug in the check, not in
# the platform, and an expensive one to diagnose because the number *looks* like a routing fault.
status_of_post() {  # $1 = full URL
  curl -s -o /dev/null -w '%{http_code}' --max-time 10 \
    -X POST -H 'Content-Type: application/json' -d '{}' "$1" || echo "000"
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

await_post_status() {  # $1 = URL, $2 = timeout seconds, $3..$n = acceptable codes
  # Bounded rather than single-shot, and for the same reason `await_endpoints_empty` is: this is the
  # one check that leaves the host and traverses the cluster's own plumbing — CoreDNS resolving the
  # Service name for the first time, kube-proxy's rules for its endpoints, the pod still being a
  # ready endpoint. Every one of those settles on its own schedule, none of them on ours, and any of
  # them missing for one second answers 502 (no connection) where the platform is in fact fine. A
  # loaded CI runner hits that window; an idle laptop does not, which is exactly the kind of failure
  # that only ever appears in CI. Retrying with a deadline keeps the assertion honest — a route that
  # is genuinely unwired never answers 400 and still fails the run.
  local url="$1" timeout="$2"; shift 2
  local deadline actual
  deadline=$((SECONDS + timeout))
  while :; do
    actual="$(status_of_post "$url")"
    for expected in "$@"; do
      if [ "$actual" = "$expected" ]; then
        echo "    POST $url -> $actual"
        return 0
      fi
    done
    [ $SECONDS -lt $deadline ] || break
    sleep 3
  done
  echo "FAIL: POST $url returned $actual within ${timeout}s, expected one of: $*" >&2
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

await_endpoints_empty() {  # $1 = Service name, $2 = timeout seconds
  # /health/ready going 503 and the pod leaving the Service endpoints are not simultaneous: kubelet
  # only marks the pod NotReady after failureThreshold x periodSeconds (3 x 10s), and the endpoint
  # controller reacts after that. Asserting it the instant readiness flips is a guaranteed flake.
  local service="$1" timeout="$2" deadline addresses
  deadline=$((SECONDS + timeout))
  while [ $SECONDS -lt $deadline ]; do
    addresses="$(kubectl -n "$NAMESPACE" get endpoints "$service" \
      -o jsonpath='{.subsets[*].addresses[*].ip}')"
    if [ -z "$addresses" ]; then
      echo "    pod removed from the Service endpoints"
      return 0
    fi
    sleep 3
  done
  echo "FAIL: pod is still a ready Service endpoint ($addresses) after ${timeout}s despite /health/ready 503" >&2
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

# `rollout status` answers "the rollout finished", which is a statement about the Deployment's
# history and not about this instant: it is satisfied by a Deployment whose pod has since gone
# NotReady. Every check below sends real traffic, so re-assert the thing those checks actually
# depend on — that every pod is Ready *now*, backing services included.
kubectl -n "$NAMESPACE" wait --for=condition=Ready pod \
  -l app.kubernetes.io/part-of=fooddeliveryservice --timeout=5m >/dev/null
echo "    all pods Ready"

echo "==> 2. the Gateway is reachable and proxies downstream"
assert_status "$GATEWAY_URL/health/ready" 200
# An empty body on the anonymous registration route: 400 means YARP forwarded it and Users rejected
# the payload, which is the proof that the route, the cluster and the Service DNS all resolve.
# A 404 here would mean the route never matched — the failure mode the Gateway's config had.
# It has to be a POST: `users/register` is a MapPost endpoint, so a GET answers 405 and proves
# only that something downstream owns the path, not that the request reached the handler.
# A 502 is the *transient* answer here — the Gateway could not open a connection to the Users
# Service — so it is waited out rather than failed on. See `await_post_status`.
await_post_status "$GATEWAY_URL/users/register" 90 400 415 422

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

# The Gateway holds a Redis connection too, since Feature 3.5 Milestone G put the rate limiter's
# counters there — and it is the single public entry point, so the blast radius of getting this
# wrong is the whole platform. The limiter fails **open**: losing Redis costs the per-client budget,
# not the gateway. So unlike a module host it must stay Ready and must keep proxying while Redis is
# down. Asserted here rather than trusted, because "we made the front door depend on the cache" is
# exactly the change that deserves a test.
#
# Both checks are deliberately downstream-independent: the module hosts *are* supposed to drain
# their endpoints while Redis is gone, so proxying anything through the gateway right now would be
# asserting the opposite of step 4. `/health/ready` is the gateway's own probe, and a path no YARP
# route matches still passes through the limiter — a 404 means the request was admitted with the
# store unreachable, where a 429 or a 500 would mean it failed closed.
assert_status "$GATEWAY_URL/health/ready" 200
assert_status "$GATEWAY_URL/no-such-route" 404
echo "    gateway still Ready and admitting requests without its rate-limit store"

await_endpoints_empty fooddeliveryservice-orders-api 120

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

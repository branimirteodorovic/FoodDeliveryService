#!/usr/bin/env bash
#
# Brings the whole solution up on a local Kubernetes cluster: creates a KinD cluster, builds all
# eight service images from the existing Dockerfiles, loads them onto the nodes, applies the
# manifests and waits for everything to report Ready.
#
# The build step is not optional ceremony: KinD nodes have no registry to pull `fooddeliveryservice/*`
# from, so every image has to be built locally and pushed onto the nodes with `kind load`.
#
# Usage:
#   ./kind-up.sh              # everything (first run: expect ~10-15 minutes, mostly image builds)
#   ./kind-up.sh --no-build   # redeploy using the images already on the nodes (fast)
set -euo pipefail

CLUSTER_NAME="fooddeliveryservice"
IMAGE_TAG="local-dev"
NAMESPACE="fooddeliveryservice"

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
backend_dir="$(cd "$script_dir/../../.." && pwd)"   # Backend/ — the Docker build context
deploy_dir="$backend_dir/deploy"

# image-name:host-project-directory
IMAGES=(
  "identity:FoodDeliveryService.Identity"
  "gateway:FoodDeliveryService.Gateway"
  "users-api:FoodDeliveryService.Users.Api"
  "orders-api:FoodDeliveryService.Orders.Api"
  "restaurants-api:FoodDeliveryService.Restaurants.Api"
  "delivery-api:FoodDeliveryService.Delivery.Api"
  "notifications-api:FoodDeliveryService.Notifications.Api"
  "realtime-api:FoodDeliveryService.RealTime.Api"
  "support-api:FoodDeliveryService.Support.Api"
)

# Every Deployment, in the order they are waited on.
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

build=true
for arg in "$@"; do
  case "$arg" in
    --no-build) build=false ;;
    *) echo "unknown argument: $arg" >&2; exit 2 ;;
  esac
done

for tool in kind kubectl docker; do
  command -v "$tool" >/dev/null 2>&1 || { echo "required tool not found: $tool" >&2; exit 1; }
done

if kind get clusters 2>/dev/null | grep -qx "$CLUSTER_NAME"; then
  echo "==> cluster '$CLUSTER_NAME' already exists — reusing it"
else
  echo "==> creating cluster '$CLUSTER_NAME'"
  kind create cluster --config "$deploy_dir/kind/kind-cluster.yaml"
fi

if [ "$build" = true ]; then
  for entry in "${IMAGES[@]}"; do
    name="${entry%%:*}"
    project="${entry##*:}"
    echo "==> building fooddeliveryservice/$name:$IMAGE_TAG"
    docker build \
      --file "$backend_dir/src/API/$project/Dockerfile" \
      --tag "fooddeliveryservice/$name:$IMAGE_TAG" \
      "$backend_dir"
    echo "==> loading fooddeliveryservice/$name:$IMAGE_TAG onto the cluster nodes"
    kind load docker-image "fooddeliveryservice/$name:$IMAGE_TAG" --name "$CLUSTER_NAME"
  done
fi

echo "==> applying namespace, config and backing services"
# The namespace goes on its own, first. `kubectl apply -f <dir>` walks the directory in **lexical
# order**, and `config.yaml` sorts before `namespace.yaml` — so on a genuinely fresh cluster the
# ConfigMap and the Secret are submitted into a namespace that does not exist yet and are rejected
# with `namespaces "fooddeliveryservice" not found`, while everything else applies cleanly. The
# script then fails, or worse, a second run "fixes" it because the namespace survives from the first.
kubectl apply -f "$deploy_dir/k8s/base/namespace.yaml"

# Feature 3.7 Milestone C. The per-service Postgres roles are defined in ONE file, which compose
# bind-mounts and which the StatefulSet mounts from this ConfigMap. Generated from that file rather
# than committed a second time as YAML: a hand-maintained copy of 190 lines of GRANTs is a drift
# trap where the failure mode is a cluster that quietly keeps the old privileges.
#
# initdb only runs it on an EMPTY data directory, so changing the SQL and re-running this script
# changes nothing — delete the StatefulSet's PVC (kind-down.sh, or
# `kubectl -n fooddeliveryservice delete pvc data-fooddeliveryservice-database-0`) first.
kubectl create configmap postgres-init   --namespace "$NAMESPACE"   --from-file="$backend_dir/docker/postgres/init/01-roles.sql"   --dry-run=client -o yaml | kubectl apply -f -

kubectl apply -f "$deploy_dir/k8s/base/"

echo "==> waiting for Postgres, Redis and RabbitMQ"
# Rollout status honours the readiness probes, so this means "Postgres is accepting connections",
# not merely "the pod was scheduled". Starting the services before the broker is up only produces a
# few minutes of retry noise in the logs.
kubectl -n "$NAMESPACE" rollout status statefulset/fooddeliveryservice-database --timeout=5m
kubectl -n "$NAMESPACE" rollout status statefulset/fooddeliveryservice-redis --timeout=5m
kubectl -n "$NAMESPACE" rollout status statefulset/fooddeliveryservice-queue --timeout=5m

echo "==> applying the services"
kubectl apply -f "$deploy_dir/k8s/services/"

# Identity first and on its own: every module host has a readiness check against it, so until it is
# up nothing else can report Ready and the waits below would all time out together.
echo "==> waiting for Identity"
kubectl -n "$NAMESPACE" rollout status deployment/fooddeliveryservice-identity --timeout=5m

echo "==> waiting for the remaining services"
for deployment in "${DEPLOYMENTS[@]}"; do
  kubectl -n "$NAMESPACE" rollout status "deployment/$deployment" --timeout=8m
done

echo
echo "Everything is up."
echo "  Gateway   http://localhost:8000    (health: http://localhost:8000/health/ready)"
echo "  Identity  http://localhost:18080   (discovery: /.well-known/openid-configuration)"
echo
echo "  kubectl -n $NAMESPACE get pods"
echo "  kubectl -n $NAMESPACE logs -f deployment/fooddeliveryservice-orders-api"

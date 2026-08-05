#!/usr/bin/env bash
#
# Tears the local cluster down. Deleting the KinD cluster deletes its nodes, and with them the
# local-path PersistentVolumes — the Postgres/Redis/RabbitMQ data is gone. That is the point: the
# cluster is disposable, and `kind-up.sh` rebuilds it from the manifests alone.
set -euo pipefail

CLUSTER_NAME="fooddeliveryservice"

command -v kind >/dev/null 2>&1 || { echo "required tool not found: kind" >&2; exit 1; }

if kind get clusters 2>/dev/null | grep -qx "$CLUSTER_NAME"; then
  echo "==> deleting cluster '$CLUSTER_NAME'"
  kind delete cluster --name "$CLUSTER_NAME"
else
  echo "cluster '$CLUSTER_NAME' does not exist — nothing to do"
fi

<#
.SYNOPSIS
Brings the whole solution up on a local Kubernetes cluster (Windows twin of kind-up.sh).

.DESCRIPTION
Creates the KinD cluster, builds all eight service images from the existing Dockerfiles, loads them
onto the nodes (KinD has no registry to pull `fooddeliveryservice/*` from), applies the manifests
and waits for everything to report Ready.

When it finishes: the Gateway is on http://localhost:8000 and Identity on http://localhost:18080.

.PARAMETER NoBuild
Redeploy using the images already loaded on the nodes. Much faster.

.EXAMPLE
./kind-up.ps1
.EXAMPLE
./kind-up.ps1 -NoBuild
#>
[CmdletBinding()]
param(
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'

$clusterName = 'fooddeliveryservice'
$imageTag = 'local-dev'
$namespace = 'fooddeliveryservice'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$backendDir = (Resolve-Path (Join-Path $scriptDir '../../..')).Path   # Backend/ — the build context
$deployDir = Join-Path $backendDir 'deploy'

$images = [ordered]@{
    'identity'          = 'FoodDeliveryService.Identity'
    'gateway'           = 'FoodDeliveryService.Gateway'
    'users-api'         = 'FoodDeliveryService.Users.Api'
    'orders-api'        = 'FoodDeliveryService.Orders.Api'
    'restaurants-api'   = 'FoodDeliveryService.Restaurants.Api'
    'delivery-api'      = 'FoodDeliveryService.Delivery.Api'
    'notifications-api' = 'FoodDeliveryService.Notifications.Api'
    'realtime-api'      = 'FoodDeliveryService.RealTime.Api'
}

$deployments = @(
    'fooddeliveryservice-identity'
    'fooddeliveryservice-users-api'
    'fooddeliveryservice-orders-api'
    'fooddeliveryservice-restaurants-api'
    'fooddeliveryservice-delivery-api'
    'fooddeliveryservice-notifications-api'
    'fooddeliveryservice-realtime-api'
    'fooddeliveryservice-gateway'
)

foreach ($tool in 'kind', 'kubectl', 'docker') {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        throw "required tool not found: $tool"
    }
}

if ((kind get clusters) -contains $clusterName) {
    Write-Host "==> cluster '$clusterName' already exists — reusing it"
}
else {
    Write-Host "==> creating cluster '$clusterName'"
    kind create cluster --config (Join-Path $deployDir 'kind/kind-cluster.yaml')
    if ($LASTEXITCODE -ne 0) { throw 'kind create cluster failed' }
}

if (-not $NoBuild) {
    foreach ($name in $images.Keys) {
        $project = $images[$name]
        $image = "fooddeliveryservice/${name}:$imageTag"

        Write-Host "==> building $image"
        docker build --file (Join-Path $backendDir "src/API/$project/Dockerfile") --tag $image $backendDir
        if ($LASTEXITCODE -ne 0) { throw "docker build failed for $image" }

        Write-Host "==> loading $image onto the cluster nodes"
        kind load docker-image $image --name $clusterName
        if ($LASTEXITCODE -ne 0) { throw "kind load failed for $image" }
    }
}

Write-Host '==> applying namespace, config and backing services'
# The namespace goes on its own, first. `kubectl apply -f <dir>` walks the directory in **lexical
# order**, and `config.yaml` sorts before `namespace.yaml` — so on a genuinely fresh cluster the
# ConfigMap and the Secret are submitted into a namespace that does not exist yet and are rejected
# with `namespaces "fooddeliveryservice" not found`, while everything else applies cleanly. The
# script then fails, or worse, a second run "fixes" it because the namespace survives from the first.
kubectl apply -f (Join-Path $deployDir 'k8s/base/namespace.yaml')
if ($LASTEXITCODE -ne 0) { throw 'kubectl apply (namespace) failed' }

kubectl apply -f (Join-Path $deployDir 'k8s/base/')
if ($LASTEXITCODE -ne 0) { throw 'kubectl apply (base) failed' }

Write-Host '==> waiting for Postgres, Redis and RabbitMQ'
# Rollout status honours the readiness probes, so this means "Postgres is accepting connections",
# not merely "the pod was scheduled".
foreach ($workload in 'statefulset/fooddeliveryservice-database',
                      'statefulset/fooddeliveryservice-redis',
                      'statefulset/fooddeliveryservice-queue') {
    kubectl -n $namespace rollout status $workload --timeout=5m
    if ($LASTEXITCODE -ne 0) { throw "$workload did not become ready" }
}

Write-Host '==> applying the services'
kubectl apply -f (Join-Path $deployDir 'k8s/services/')
if ($LASTEXITCODE -ne 0) { throw 'kubectl apply (services) failed' }

# Identity first and on its own: every module host has a readiness check against it, so until it is
# up nothing else can report Ready and the waits below would all time out together.
Write-Host '==> waiting for Identity'
kubectl -n $namespace rollout status deployment/fooddeliveryservice-identity --timeout=5m
if ($LASTEXITCODE -ne 0) { throw 'Identity did not become ready' }

Write-Host '==> waiting for the remaining services'
foreach ($deployment in $deployments) {
    kubectl -n $namespace rollout status "deployment/$deployment" --timeout=8m
    if ($LASTEXITCODE -ne 0) { throw "$deployment did not become ready" }
}

Write-Host ''
Write-Host 'Everything is up.'
Write-Host '  Gateway   http://localhost:8000    (health: http://localhost:8000/health/ready)'
Write-Host '  Identity  http://localhost:18080   (discovery: /.well-known/openid-configuration)'
Write-Host ''
Write-Host "  kubectl -n $namespace get pods"
Write-Host "  kubectl -n $namespace logs -f deployment/fooddeliveryservice-orders-api"

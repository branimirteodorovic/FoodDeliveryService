<#
.SYNOPSIS
Deletes the local KinD cluster (Windows/PowerShell twin of kind-down.sh).

.DESCRIPTION
Deleting the cluster deletes its nodes and with them the local-path PersistentVolumes, so the
Postgres/Redis/RabbitMQ data goes too. That is intended: the cluster is disposable and kind-up
rebuilds it from the manifests alone.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$clusterName = 'fooddeliveryservice'

if (-not (Get-Command kind -ErrorAction SilentlyContinue)) {
    throw 'required tool not found: kind'
}

if ((kind get clusters) -contains $clusterName) {
    Write-Host "==> deleting cluster '$clusterName'"
    kind delete cluster --name $clusterName
    if ($LASTEXITCODE -ne 0) { throw 'kind delete cluster failed' }
}
else {
    Write-Host "cluster '$clusterName' does not exist — nothing to do"
}

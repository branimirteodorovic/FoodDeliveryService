<#
.SYNOPSIS
Runs a k6 script against the platform (Windows twin of run.sh).

.DESCRIPTION
By default the generator runs *inside* the compose network (the `loadtest` compose profile), which is
the mode every published number should come from: it takes Docker's host port-forwarding out of the
measurement and it is the only mode in which the service DNS names resolve.

The stack must already be up: `docker-compose up -d` from Backend/.

.PARAMETER Script
The k6 script, relative to loadtest/. Defaults to smoke.js.

.PARAMETER Environment
compose (default) | compose-host | kind. Implied by -Local when not given.

.PARAMETER Profile
Milestone D profile name, passed through as -e PROFILE.

.PARAMETER RunId
Names the run. Every correlation id carries it, so Seq can be filtered to one run.

.PARAMETER Local
Use the k6 binary on PATH instead of the compose profile, targeting :3000 / :18080.

.PARAMETER K6Args
Everything else goes straight to k6.

.EXAMPLE
./run.ps1
.EXAMPLE
./run.ps1 -Local
.EXAMPLE
./run.ps1 scenarios/browse.js -Profile baseline
.EXAMPLE
./run.ps1 -K6Args '--vus','20','--duration','2m'
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Script = 'smoke.js',
    [string]$Environment,
    [string]$Profile,
    [string]$RunId,
    [switch]$Local,
    [string[]]$K6Args = @()
)

$ErrorActionPreference = 'Stop'

$composeService = 'fooddeliveryservice.k6'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$loadtestDir = (Resolve-Path (Join-Path $scriptDir '..')).Path
$backendDir = (Resolve-Path (Join-Path $loadtestDir '..')).Path

# A run id every correlation id carries, so `CorrelationId like 'loadtest-<run-id>-%'` in Seq returns
# this run and nothing else. Generated here rather than in the script because k6 evaluates init code
# once per VU, and a per-VU fallback would give one run several ids.
if (-not $RunId) {
    $RunId = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
}

if (-not $Environment) {
    $Environment = if ($Local) { 'compose-host' } else { 'compose' }
}

$k6Env = @('-e', "ENV=$Environment", '-e', "RUN_ID=$RunId")
if ($Profile) { $k6Env += @('-e', "PROFILE=$Profile") }

# Passed through when set, never defaulted here — config/environments.js owns the defaults, so the
# two runner scripts cannot drift apart from them or from each other.
#
# The credentials carry a LOADTEST_ prefix because k6 folds the whole system environment into __ENV
# and `USERNAME` is set on every Windows machine: a bare name would silently log in as whoever is at
# the keyboard and report the resulting 100% failure rate as a platform fault.
foreach ($name in 'LOADTEST_USERNAME', 'LOADTEST_PASSWORD', 'GATEWAY_URL', 'IDENTITY_URL') {
    $value = [Environment]::GetEnvironmentVariable($name)
    if ($value) { $k6Env += @('-e', "$name=$value") }
}

# The profile is part of the artifact's name, not just its contents: `results/` fills up with runs
# whose only difference is the shape of the load, and a baseline that cannot be told from the ramp
# next to it without opening both is an artifact nobody trusts.
$profileTag = if ($Profile) { $Profile } else { 'noprofile' }
$summary = "results/$(Split-Path -Leaf $Script)-$profileTag-$RunId.summary.json"
$resultsDir = Join-Path $loadtestDir 'results'
if (-not (Test-Path $resultsDir)) { New-Item -ItemType Directory -Path $resultsDir | Out-Null }

$profileNote = if ($Profile) { " · profile '$Profile'" } else { '' }
Write-Host "==> run '$RunId' · script '$Script' · env '$Environment'$profileNote"

if ($Local) {
    if (-not (Get-Command k6 -ErrorAction SilentlyContinue)) {
        throw 'k6 not found on PATH — install it, or drop -Local to use the compose profile'
    }

    Push-Location $loadtestDir
    try {
        & k6 run @k6Env --summary-export $summary @K6Args $Script
        if ($LASTEXITCODE -ne 0) { throw "k6 exited with $LASTEXITCODE (threshold breach, or the run failed)" }
    }
    finally { Pop-Location }

    return
}

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw 'docker not found on PATH'
}

Push-Location $backendDir
try {
    & docker compose --profile loadtest run --rm $composeService `
        run @k6Env --summary-export "/loadtest/$summary" @K6Args "/loadtest/$Script"
    if ($LASTEXITCODE -ne 0) { throw "k6 exited with $LASTEXITCODE (threshold breach, or the run failed)" }
}
finally { Pop-Location }

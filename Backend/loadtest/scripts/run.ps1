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

.PARAMETER Prometheus
Stream the run into the Feature 2.4 Prometheus (remote write) so it can be watched live on the
`fds-load` Grafana dashboard, and capture the platform's own series next to the summary afterwards.
Off by default: it is another container competing for the host the system under test is running on.

.PARAMETER K6Args
Everything else goes straight to k6.

.EXAMPLE
./run.ps1
.EXAMPLE
./run.ps1 -Local
.EXAMPLE
./run.ps1 scenarios/browse.js -Profile baseline
.EXAMPLE
./run.ps1 scenarios/mixed.js -Profile ramp -Prometheus
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
    [switch]$Prometheus,
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
#
# `handleSummary()` (config/output.js) writes `<base>.summary.json` and `<base>.summary.md` itself,
# which is why the deprecated `--summary-export` is gone: it wrote one file and could not have written
# the markdown. The base is absolute for a container run — k6's working directory is /home/k6, not the
# mounted /loadtest, so a relative path would put the run's only durable record inside a container
# that `--rm` deletes three seconds later.
$profileTag = if ($Profile) { $Profile } else { 'noprofile' }
$summaryBase = "results/$(Split-Path -Leaf $Script)-$profileTag-$RunId"
$resultsDir = Join-Path $loadtestDir 'results'
if (-not (Test-Path $resultsDir)) { New-Item -ItemType Directory -Path $resultsDir | Out-Null }

# Feature 2.4's Prometheus, addressed from wherever k6 is running — the same split as
# config/environments.js, which cannot own this one because the URL is needed before k6 starts.
# Remote write is off unless asked for: it is a second container competing for the same host CPU as
# the system under test, and every published number so far was measured without it.
$prometheusUrl = if ($env:PROMETHEUS_URL) { $env:PROMETHEUS_URL }
                 elseif ($Local) { 'http://localhost:9090' }
                 else { 'http://fooddeliveryservice.prometheus:9090' }

if ($Prometheus) {
    # Prometheus rejects remote writes unless started with --web.enable-remote-write-receiver; the
    # compose file passes it. Without it k6 logs a write error per flush and the dashboard stays empty.
    $K6Args = @('-o', 'experimental-prometheus-rw') + $K6Args
}

$profileNote = if ($Profile) { " · profile '$Profile'" } else { '' }
$prometheusNote = if ($Prometheus) { " · streaming to $prometheusUrl" } else { '' }
Write-Host "==> run '$RunId' · script '$Script' · env '$Environment'$profileNote$prometheusNote"

$startedAt = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$exitCode = 0

if ($Local) {
    if (-not (Get-Command k6 -ErrorAction SilentlyContinue)) {
        throw 'k6 not found on PATH — install it, or drop -Local to use the compose profile'
    }

    if ($Prometheus) {
        if (-not $env:K6_PROMETHEUS_RW_SERVER_URL) { $env:K6_PROMETHEUS_RW_SERVER_URL = "$prometheusUrl/api/v1/write" }
        if (-not $env:K6_PROMETHEUS_RW_TREND_STATS) { $env:K6_PROMETHEUS_RW_TREND_STATS = 'p(95),p(99),avg,max' }
        if (-not $env:K6_PROMETHEUS_RW_STALE_MARKERS) { $env:K6_PROMETHEUS_RW_STALE_MARKERS = 'true' }
    }

    Push-Location $loadtestDir
    try {
        & k6 run @k6Env -e "SUMMARY_BASE=$summaryBase" @K6Args $Script
        $exitCode = $LASTEXITCODE
    }
    finally { Pop-Location }
}
else {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        throw 'docker not found on PATH'
    }

    # The K6_PROMETHEUS_RW_* defaults live in the compose service, next to the Prometheus it names,
    # and are inert unless -o selects that output. Only an override has to be passed through here.
    $composeEnv = @()
    if ($env:PROMETHEUS_URL) {
        $composeEnv += @('-e', "K6_PROMETHEUS_RW_SERVER_URL=$prometheusUrl/api/v1/write")
    }

    Push-Location $backendDir
    try {
        & docker compose --profile loadtest run --rm @composeEnv $composeService `
            run @k6Env -e "SUMMARY_BASE=/loadtest/$summaryBase" @K6Args "/loadtest/$Script"
        $exitCode = $LASTEXITCODE
    }
    finally { Pop-Location }
}

# ── The platform's own numbers, captured while they still exist ────────────────────────────────
#
# Prometheus keeps 7 days on a volume docker-compose.yml calls disposable, so the server-side half of
# a run — the half that says *where* it slowed down — is gone long before anyone writes it up. k6's
# summary has the client's view and nothing else. This pulls the same series the `fds-load` dashboard
# draws into one file next to the summary.
#
# Grafana PNG export is deliberately not attempted: it needs the grafana-image-renderer plugin, which
# this stack does not install. Use Grafana's own share menu for pictures; this is the data behind them.
if ($Prometheus) {
    $hostUrl = if ($env:PROMETHEUS_URL) { $env:PROMETHEUS_URL } else { 'http://localhost:9090' }
    $endedAt = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()

    $queries = [ordered]@{
        app_request_p95_by_service    = 'histogram_quantile(0.95, sum by (le, service_name) (rate(app_request_duration_seconds_bucket[1m])))'
        app_requests_per_second       = 'sum by (service_name) (rate(app_requests_total[1m]))'
        http_5xx_per_second           = 'sum by (service_name) (rate(http_server_request_duration_seconds_count{http_response_status_code=~"5.."}[1m]))'
        orders_placed_per_second      = 'sum(rate(orders_placed_total[1m]))'
        order_transitions_per_second  = 'sum by (to) (rate(orders_state_transition_total[1m]))'
        assignment_outcome_per_second = 'sum by (outcome) (rate(delivery_assignment_outcome_total[1m]))'
        cache_hit_rate                = '100 * sum(rate(cache_hits_total[1m])) / (sum(rate(cache_hits_total[1m])) + sum(rate(cache_misses_total[1m])))'
    }

    $series = [ordered]@{}

    foreach ($name in $queries.Keys) {
        $uri = "$hostUrl/api/v1/query_range?query=$([Uri]::EscapeDataString($queries[$name]))" +
               "&start=$startedAt&end=$endedAt&step=15s"

        try {
            $series[$name] = (Invoke-WebRequest -Uri $uri -UseBasicParsing).Content | ConvertFrom-Json
        }
        catch {
            $series[$name] = [pscustomobject]@{ status = 'error'; error = $_.Exception.Message }
        }
    }

    $capture = [ordered]@{
        runId       = $RunId
        script      = $Script
        profile     = $profileTag
        environment = $Environment
        start       = $startedAt
        end         = $endedAt
        step        = '15s'
        series      = $series
    }

    $capturePath = Join-Path $loadtestDir "$summaryBase.platform.json"
    $capture | ConvertTo-Json -Depth 12 | Set-Content -Path $capturePath -Encoding utf8
    Write-Host "==> platform series captured to $summaryBase.platform.json"
}

if ($exitCode -ne 0) {
    throw "k6 exited with $exitCode (threshold breach, or the run failed)"
}

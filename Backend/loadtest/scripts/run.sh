#!/usr/bin/env bash
#
# Runs a k6 script against the platform. Profile name in, environment variables and output flags out.
#
# By default the generator runs *inside* the compose network (the `loadtest` compose profile), which
# is the mode every published number should come from: it takes Docker's host port-forwarding out of
# the measurement and it is the only mode in which the service DNS names resolve.
#
# Usage:
#   ./run.sh                                  # smoke.js, inside compose
#   ./run.sh scenarios/browse.js              # a different script (Milestone C onwards)
#   ./run.sh --local                          # k6 from the host against :3000 / :18080
#   ./run.sh --profile baseline               # Milestone D profiles
#   ./run.sh --run-id nightly-01              # name the run (shows up in every correlation id)
#   ./run.sh --prometheus                     # stream to Prometheus + capture the platform's series
#   ./run.sh -- --vus 20 --duration 2m        # everything after `--` goes straight to k6
#
# The stack must already be up: `docker-compose up -d` from Backend/.
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
loadtest_dir="$(cd "$script_dir/.." && pwd)"
backend_dir="$(cd "$loadtest_dir/.." && pwd)"

COMPOSE_SERVICE="fooddeliveryservice.k6"

script="smoke.js"
env_name=""
profile=""
run_id=""
local_run=false
prometheus=false
k6_args=()

case "${LOADTEST_PROMETHEUS:-}" in
  1 | true | yes) prometheus=true ;;
esac

while [ $# -gt 0 ]; do
  case "$1" in
    --local)      local_run=true; shift ;;
    --env)        env_name="$2"; shift 2 ;;
    --profile)    profile="$2"; shift 2 ;;
    --run-id)     run_id="$2"; shift 2 ;;
    --prometheus) prometheus=true; shift ;;
    --help|-h)    sed -n '2,22p' "${BASH_SOURCE[0]}"; exit 0 ;;
    --)           shift; k6_args=("$@"); break ;;
    -*)           echo "unknown option: $1" >&2; exit 2 ;;
    *)            script="$1"; shift ;;
  esac
done

# A run id every correlation id carries, so `CorrelationId like 'loadtest-<run-id>-%'` in Seq returns
# this run and nothing else. Generated here rather than in the script because k6 evaluates init code
# once per VU, and a per-VU fallback would give one run several ids.
if [ -z "$run_id" ]; then
  run_id="$(date -u +%Y%m%dT%H%M%SZ)"
fi

if [ -z "$env_name" ]; then
  if [ "$local_run" = true ]; then env_name="compose-host"; else env_name="compose"; fi
fi

k6_env=(-e "ENV=$env_name" -e "RUN_ID=$run_id")

if [ -n "$profile" ]; then
  k6_env+=(-e "PROFILE=$profile")
fi

# Passed through when set, never defaulted here — config/environments.js owns the defaults, so the
# two runner scripts cannot drift apart from them or from each other.
#
# The credentials carry a LOADTEST_ prefix because k6 folds the whole system environment into __ENV
# and `USERNAME` is set on every Windows machine: a bare name would silently log in as whoever is at
# the keyboard and report the resulting 100% failure rate as a platform fault.
#
# `if` rather than `[ -n … ] && …`: under `set -e` an AND-list whose test fails on the final loop
# iteration leaves the whole `for` with a non-zero status, which is a script that exits before it
# runs anything depending on the shell's version.
for name in LOADTEST_USERNAME LOADTEST_PASSWORD GATEWAY_URL IDENTITY_URL; do
  eval "value=\${$name:-}"

  if [ -n "$value" ]; then
    k6_env+=(-e "$name=$value")
  fi
done

# The profile is part of the artifact's name, not just its contents: `results/` fills up with runs
# whose only difference is the shape of the load, and a baseline that cannot be told from the ramp
# next to it without opening both is an artifact nobody trusts.
#
# `handleSummary()` (config/output.js) writes `<base>.summary.json` and `<base>.summary.md` itself,
# which is why the deprecated `--summary-export` is gone: it wrote one file and could not have
# written the markdown. The base is absolute for a container run — k6's working directory is
# /home/k6, not the mounted /loadtest, so a relative path would put the run's only durable record
# inside a container that `--rm` deletes three seconds later.
summary_base="results/${script##*/}-${profile:-noprofile}-${run_id}"

# Feature 2.4's Prometheus, addressed from wherever k6 is running — the same split as
# config/environments.js, which cannot own this one because the URL is needed before k6 starts.
# Remote write is **off unless asked for**: it is a second container competing for the same host
# CPU as the system under test, and every published number so far was measured without it.
if [ "$local_run" = true ]; then
  prometheus_url="${PROMETHEUS_URL:-http://localhost:9090}"
else
  prometheus_url="${PROMETHEUS_URL:-http://fooddeliveryservice.prometheus:9090}"
fi

prometheus_note=""

if [ "$prometheus" = true ]; then
  # Prometheus rejects remote writes unless started with --web.enable-remote-write-receiver; the
  # compose file passes it. Without it k6 logs a write error per flush and the dashboard stays empty.
  k6_args=(-o experimental-prometheus-rw "${k6_args[@]}")
  prometheus_note=" · streaming to $prometheus_url"
fi

echo "==> run '$run_id' · script '$script' · env '$env_name'${profile:+ · profile '$profile'}$prometheus_note"

started_at="$(date -u +%s)"

# Not `exec` any more: the platform-side capture below has to happen after k6 exits, and a threshold
# breach (exit 99) is a result to record rather than a reason to skip recording it.
status=0
mkdir -p "$loadtest_dir/results"

if [ "$local_run" = true ]; then
  command -v k6 >/dev/null 2>&1 || { echo "k6 not found on PATH — install it, or drop --local to use the compose profile" >&2; exit 1; }

  if [ "$prometheus" = true ]; then
    export K6_PROMETHEUS_RW_SERVER_URL="${K6_PROMETHEUS_RW_SERVER_URL:-$prometheus_url/api/v1/write}"
    export K6_PROMETHEUS_RW_TREND_STATS="${K6_PROMETHEUS_RW_TREND_STATS:-p(95),p(99),avg,max}"
    export K6_PROMETHEUS_RW_STALE_MARKERS="${K6_PROMETHEUS_RW_STALE_MARKERS:-true}"
  fi

  cd "$loadtest_dir"
  k6 run "${k6_env[@]}" -e "SUMMARY_BASE=$summary_base" "${k6_args[@]}" "$script" || status=$?
else
  command -v docker >/dev/null 2>&1 || { echo "docker not found on PATH" >&2; exit 1; }

  # Git Bash on Windows rewrites anything that looks like a POSIX path into a Windows one before the
  # process sees it, so `/loadtest/smoke.js` reaches k6 as `C:/Program Files/Git/loadtest/smoke.js` and
  # the run dies on a missing file. These are container paths; nothing here should be translated.
  # (run.ps1 is the native Windows entry point — this only keeps Git Bash from being a trap.)
  export MSYS2_ARG_CONV_EXCL='*'
  export MSYS_NO_PATHCONV=1

  # The K6_PROMETHEUS_RW_* defaults live in the compose service, next to the Prometheus it names, and
  # are inert unless -o selects that output. Only an override has to be passed through here.
  compose_env=()
  if [ -n "${PROMETHEUS_URL:-}" ]; then
    compose_env+=(-e "K6_PROMETHEUS_RW_SERVER_URL=$prometheus_url/api/v1/write")
  fi

  cd "$backend_dir"
  docker compose --profile loadtest run --rm "${compose_env[@]}" "$COMPOSE_SERVICE" \
    run "${k6_env[@]}" -e "SUMMARY_BASE=/loadtest/$summary_base" "${k6_args[@]}" "/loadtest/$script" || status=$?
fi

# ── The platform's own numbers, captured while they still exist ────────────────────────────────
#
# Prometheus keeps 7 days on a volume docker-compose.yml calls disposable, so the server-side half of
# a run — the half that says *where* it slowed down — is gone long before anyone writes it up. k6's
# summary has the client's view and nothing else. This pulls the same series the `fds-load` dashboard
# draws into one file next to the summary.
#
# Grafana PNG export is deliberately not attempted: it needs the grafana-image-renderer plugin, which
# this stack does not install. Use Grafana's own share menu for pictures; this is the data behind them.
capture_platform() {
  local out="$loadtest_dir/$summary_base.platform.json"
  local host_url="${PROMETHEUS_URL:-http://localhost:9090}"
  local ended_at
  ended_at="$(date -u +%s)"

  command -v curl >/dev/null 2>&1 || { echo "curl not found — skipping the platform capture" >&2; return; }

  local queries=(
    "app_request_p95_by_service=histogram_quantile(0.95, sum by (le, service_name) (rate(app_request_duration_seconds_bucket[1m])))"
    "app_requests_per_second=sum by (service_name) (rate(app_requests_total[1m]))"
    "http_5xx_per_second=sum by (service_name) (rate(http_server_request_duration_seconds_count{http_response_status_code=~\"5..\"}[1m]))"
    "orders_placed_per_second=sum(rate(orders_placed_total[1m]))"
    "order_transitions_per_second=sum by (to) (rate(orders_state_transition_total[1m]))"
    "assignment_outcome_per_second=sum by (outcome) (rate(delivery_assignment_outcome_total[1m]))"
    "cache_hit_rate=100 * sum(rate(cache_hits_total[1m])) / (sum(rate(cache_hits_total[1m])) + sum(rate(cache_misses_total[1m])))"
  )

  {
    printf '{\n  "runId": "%s",\n  "script": "%s",\n  "profile": "%s",\n' "$run_id" "$script" "${profile:-noprofile}"
    printf '  "environment": "%s",\n  "start": %s,\n  "end": %s,\n  "step": "15s",\n  "series": {\n' \
      "$env_name" "$started_at" "$ended_at"

    local first=1
    for entry in "${queries[@]}"; do
      local key="${entry%%=*}"
      local expr="${entry#*=}"
      local body

      if ! body="$(curl -fsG "$host_url/api/v1/query_range" \
        --data-urlencode "query=$expr" \
        --data-urlencode "start=$started_at" \
        --data-urlencode "end=$ended_at" \
        --data-urlencode "step=15s" 2>/dev/null)"; then
        body='{"status":"error","error":"query failed"}'
      fi

      [ $first -eq 1 ] || printf ',\n'
      first=0
      printf '    "%s": %s' "$key" "$body"
    done

    printf '\n  }\n}\n'
  } >"$out"

  echo "==> platform series captured to $summary_base.platform.json"
}

if [ "$prometheus" = true ]; then
  capture_platform
fi

exit "$status"

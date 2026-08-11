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
k6_args=()

while [ $# -gt 0 ]; do
  case "$1" in
    --local)   local_run=true; shift ;;
    --env)     env_name="$2"; shift 2 ;;
    --profile) profile="$2"; shift 2 ;;
    --run-id)  run_id="$2"; shift 2 ;;
    --help|-h) sed -n '2,20p' "${BASH_SOURCE[0]}"; exit 0 ;;
    --)        shift; k6_args=("$@"); break ;;
    -*)        echo "unknown option: $1" >&2; exit 2 ;;
    *)         script="$1"; shift ;;
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
summary="results/${script##*/}-${profile:-noprofile}-${run_id}.summary.json"

echo "==> run '$run_id' · script '$script' · env '$env_name'${profile:+ · profile '$profile'}"

if [ "$local_run" = true ]; then
  command -v k6 >/dev/null 2>&1 || { echo "k6 not found on PATH — install it, or drop --local to use the compose profile" >&2; exit 1; }
  mkdir -p "$loadtest_dir/results"
  cd "$loadtest_dir"
  exec k6 run "${k6_env[@]}" --summary-export "$summary" "${k6_args[@]}" "$script"
fi

command -v docker >/dev/null 2>&1 || { echo "docker not found on PATH" >&2; exit 1; }
mkdir -p "$loadtest_dir/results"

# Git Bash on Windows rewrites anything that looks like a POSIX path into a Windows one before the
# process sees it, so `/loadtest/smoke.js` reaches k6 as `C:/Program Files/Git/loadtest/smoke.js` and
# the run dies on a missing file. These are container paths; nothing here should be translated.
# (run.ps1 is the native Windows entry point — this only keeps Git Bash from being a trap.)
export MSYS2_ARG_CONV_EXCL='*'
export MSYS_NO_PATHCONV=1

cd "$backend_dir"
exec docker compose --profile loadtest run --rm "$COMPOSE_SERVICE" \
  run "${k6_env[@]}" --summary-export "/loadtest/$summary" "${k6_args[@]}" "/loadtest/$script"

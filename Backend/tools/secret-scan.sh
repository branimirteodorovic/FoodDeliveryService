#!/usr/bin/env bash
# Secret scan over the tracked tree — Feature 3.7 (Final Production Hardening), Milestone B.
#
# Runs gitleaks against exactly the files git knows about, using the repository's .gitleaks.toml.
# CI calls this from the `secrets` job; run it locally the same way before pushing:
#
#   bash Backend/tools/secret-scan.sh
#
# Why an export rather than pointing gitleaks at the checkout: `gitleaks dir` walks the filesystem,
# which on a developer machine means bin/, obj/, TestResults/ and — still on disk here — the build
# output of the reverted FraudDetection host. Those are untracked, cannot have been committed, and
# would produce phantom findings that train everyone to ignore this job. `git archive HEAD` is the
# committed tree and nothing else, which is precisely the surface the "no secrets in Git" rule is
# about.
#
# Exit code is gitleaks': 0 clean, 1 findings, anything else a scanner failure.
set -euo pipefail

GITLEAKS_IMAGE="${GITLEAKS_IMAGE:-ghcr.io/gitleaks/gitleaks:v8.24.0}"

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
export_directory="$(mktemp -d)"
trap 'rm -rf "$export_directory"' EXIT

git -C "$repository_root" archive HEAD | tar -x -C "$export_directory"

# Copied from the working tree rather than taken from the export, so a local run exercises the
# config you are editing rather than the one already committed.
cp "$repository_root/.gitleaks.toml" "$export_directory/.gitleaks.toml"

file_count=$(find "$export_directory" -type f | wc -l)
echo "scanning ${file_count} tracked file(s) from $(git -C "$repository_root" rev-parse --short HEAD)"

# Docker on Windows needs a Windows path on the host side of the mount, and MSYS_NO_PATHCONV keeps
# Git Bash from rewriting the container side. Both are no-ops on Linux CI.
mount_source="$export_directory"
if command -v cygpath >/dev/null 2>&1; then
  mount_source="$(cygpath -w "$export_directory")"
fi

MSYS_NO_PATHCONV=1 docker run --rm \
  -v "${mount_source}:/scan" \
  "$GITLEAKS_IMAGE" dir /scan \
  --config /scan/.gitleaks.toml \
  --redact \
  --no-banner \
  --verbose

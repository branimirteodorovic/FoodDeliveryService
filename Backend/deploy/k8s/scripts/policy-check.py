#!/usr/bin/env python3
"""Policy gate over the raw manifests in deploy/k8s (Feature 2.5, Milestone A).

`kubeconform` answers "is this valid Kubernetes YAML"; this answers "is it the shape this platform
decided on". Both run in CI as required checks. Milestone C replaces this script with
helm-unittest assertions against the chart, and Milestone B adds the "no secret literal" rule.

Rules
-----
Every workload (Deployment/StatefulSet):
  * no `:latest` and no untagged image  — a rollout must be reproducible
  * every container sets resource requests AND limits

Every *application* container (identified by a port named `http` — the .NET hosts, not the
backing services):
  * pod-level runAsNonRoot: true WITH a numeric runAsUser
  * container drops all capabilities and disallows privilege escalation
  * livenessProbe  -> GET /health/live   (Feature 2.4's contract, verbatim)
    readinessProbe -> GET /health/ready
    startupProbe   -> GET /health/live
  * ASPNETCORE_HTTPS_PORTS is absent (no certificate in the pod; TLS terminates outside it)
  * ASPNETCORE_ENVIRONMENT is set, directly or through the shared ConfigMap
  * no credential is a literal: anything named like a connection string, password or secret must
    come from a `secretKeyRef`. The backing services (Postgres, RabbitMQ) are exempt — their
    passwords are the ones being handed *out*, and they have no `http` port.

Usage: python3 policy-check.py [manifest-root]   (default: the deploy/k8s directory above this one)
"""

from __future__ import annotations

import pathlib
import sys

import yaml

WORKLOAD_KINDS = {"Deployment", "StatefulSet", "DaemonSet", "Job", "CronJob"}
PROBE_PATHS = {
    "livenessProbe": "/health/live",
    "readinessProbe": "/health/ready",
    "startupProbe": "/health/live",
}

failures: list[str] = []


def fail(where: str, message: str) -> None:
    failures.append(f"{where}: {message}")


def check_image(where: str, container: dict) -> None:
    image = container.get("image", "")
    if ":" not in image.rsplit("/", 1)[-1]:
        fail(where, f"image '{image}' has no tag — pin an explicit tag")
    elif image.endswith(":latest"):
        fail(where, f"image '{image}' uses :latest — pin an explicit tag")


def check_resources(where: str, container: dict) -> None:
    resources = container.get("resources") or {}
    for section in ("requests", "limits"):
        values = resources.get(section) or {}
        for key in ("cpu", "memory"):
            if key not in values:
                fail(where, f"missing resources.{section}.{key}")


def check_security(where: str, pod_spec: dict, container: dict) -> None:
    pod_security = pod_spec.get("securityContext") or {}
    if pod_security.get("runAsNonRoot") is not True:
        fail(where, "pod securityContext.runAsNonRoot must be true")
    run_as_user = pod_security.get("runAsUser")
    if not isinstance(run_as_user, int):
        fail(where, "pod securityContext.runAsUser must be an explicit numeric UID")

    container_security = container.get("securityContext") or {}
    if container_security.get("allowPrivilegeEscalation") is not False:
        fail(where, "container securityContext.allowPrivilegeEscalation must be false")
    dropped = ((container_security.get("capabilities") or {}).get("drop")) or []
    if "ALL" not in dropped:
        fail(where, "container must drop ALL capabilities")


def check_probes(where: str, container: dict) -> None:
    for probe_name, expected_path in PROBE_PATHS.items():
        probe = container.get(probe_name)
        if not probe:
            fail(where, f"missing {probe_name}")
            continue
        http_get = probe.get("httpGet") or {}
        actual = http_get.get("path")
        if actual != expected_path:
            fail(where, f"{probe_name} probes '{actual}' — the contract is '{expected_path}'")


CREDENTIAL_MARKERS = ("connectionstrings", "password", "secret")


def check_env(where: str, container: dict) -> None:
    env = {entry.get("name"): entry for entry in container.get("env") or []}
    if "ASPNETCORE_HTTPS_PORTS" in env:
        fail(where, "ASPNETCORE_HTTPS_PORTS must not be set — there is no certificate in the pod")

    # Either inline or inherited from the shared ConfigMap, which is where it actually lives.
    from_config_map = any(
        (source.get("configMapRef") or {}).get("name") == "platform-config"
        for source in container.get("envFrom") or []
    )
    if "ASPNETCORE_ENVIRONMENT" not in env and not from_config_map:
        fail(where, "ASPNETCORE_ENVIRONMENT must be set, inline or via the platform-config ConfigMap")

    for name, entry in env.items():
        lowered = (name or "").lower()
        if any(marker in lowered for marker in CREDENTIAL_MARKERS) and "value" in entry:
            fail(where, f"{name} is a literal value — credentials must come from a secretKeyRef")


def is_application_container(container: dict) -> bool:
    return any(port.get("name") == "http" for port in container.get("ports") or [])


def check_document(path: pathlib.Path, document: dict) -> None:
    if not document or document.get("kind") not in WORKLOAD_KINDS:
        return

    name = (document.get("metadata") or {}).get("name", "<unnamed>")
    pod_spec = (((document.get("spec") or {}).get("template") or {}).get("spec")) or {}

    for container in pod_spec.get("containers") or []:
        where = f"{path.name} {document['kind']}/{name}[{container.get('name')}]"
        check_image(where, container)
        check_resources(where, container)
        if is_application_container(container):
            check_security(where, pod_spec, container)
            check_probes(where, container)
            check_env(where, container)


def main() -> int:
    root = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else pathlib.Path(__file__).resolve().parent.parent
    manifests = sorted(p for p in root.rglob("*.yaml") if p.is_file())
    if not manifests:
        print(f"no manifests found under {root}", file=sys.stderr)
        return 1

    checked = 0
    for path in manifests:
        for document in yaml.safe_load_all(path.read_text(encoding="utf-8")):
            if document and document.get("kind") in WORKLOAD_KINDS:
                checked += 1
            check_document(path, document)

    if failures:
        print(f"policy check FAILED ({len(failures)} violation(s)):", file=sys.stderr)
        for failure in failures:
            print(f"  - {failure}", file=sys.stderr)
        return 1

    print(f"policy check passed — {checked} workload(s) under {root}")
    return 0


if __name__ == "__main__":
    sys.exit(main())

---
title: "Security Testing and Evidence Specification"
type: technical-specification
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on scanner, evidence, exception, AI-routing, or release-policy change"
cis:
  stable_id: change-impact-studio:spec:security-testing-evidence
---

# Security testing and evidence specification

## Intent

CIS turns static analysis, SAST, secret scanning, filesystem and configuration scanning, container-image scanning, and DAST into queryable, revision-bound evidence. Scanner output remains authoritative. A local model may summarize normalized redacted findings for routing, but cannot change a severity, exception, or verdict.

## Canonical and derived state

`references/security-suite-profile.md` defines applicable suites. `references/accepted-security-findings.md` records exact, expiring, human-approved exceptions. Both are canonical Markdown. Inventory, run manifests, normalized findings, artifact hashes, and summaries are derived beneath `.cis/local/security/`.

```text
.cis/local/security/
  inventory.json
  results/
  runs/<workflow-run-id>/
    manifest.json
    findings.json
    artifacts.json
    summary.md
    summary-metadata.json
```

## Scanner model

| Category | Portable default | Evidence adapter | Minimum use |
|---|---|---|---|
| Static analysis and SAST | Semgrep | Semgrep JSON or SARIF | Production source |
| Secrets | Gitleaks with redaction | Gitleaks JSON | Every repository and full revision |
| Filesystem dependencies | Trivy filesystem | Trivy JSON | Dependency-bearing source |
| Deployment configuration | Trivy config | Trivy JSON | Compose, Terraform, and comparable configuration |
| Container image | Trivy image | Trivy JSON | Exact releaseable image identity before promotion |
| Dynamic application security | OWASP ZAP | ZAP JSON | Applicable externally reachable web/API runtime |

CodeQL may complement portable SAST where GitHub Code Security is enabled. Its availability never removes the portable gate. ZAP is DAST, not SAST.

## Reconciliation and verdict

`cis workflow run` executes repository-owned commands with attempt-specific logs. `cis security reconcile --run <id>` parses declared results, normalizes paths and severity, redacts secret material, hashes evidence, and correlates it with suite, component, workflow run, attempt, repository revision, and profile digest. A successful process without readable expected evidence is `invalid-evidence`.

Applicable unresolved findings at configured failure severities fail the run. An unavailable required suite makes it incomplete. Release verification must also reject stale revisions, expired or malformed exceptions, and promoted images that differ from the scanned identity.

## Exception governance

An exception identifies one scanner and one exact normalized fingerprint. It records classification, bounded reason, expiry, accountable owner, human approver, and approval reference. Wildcards, indefinite exceptions, model approval, and broad path/rule suppressions are invalid.

## Local AI triage

`cis security summarise` uses a local provider only. The prompt receives normalized redacted findings; secret-category messages are replaced with a fixed safe description. Metadata records provider, model, prompt and manifest digests, local-only routing, and deterministic-verdict preservation. `--no-llm` produces the deterministic report only.

## Release boundary

Security evidence is mechanical and adds no approval checkpoint. Completion requires all applicable suites to pass or have bounded approved exceptions. Images are built once, identified, scanned, and promoted without rebuild. Security artifacts are retained according to repository policy without committing `.cis/local/`.

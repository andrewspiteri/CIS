---
title: "cis security reconcile"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-security-reconcile
---

# `cis security reconcile`

Normalize scanner results from one exact workflow run and preserve revision-bound evidence.

```text
cis security reconcile --run <workflow-run-id> [--repo <path>] [--format <human|json|agent>]
```

Supported inputs are SARIF, Semgrep JSON, redacted Gitleaks JSON, Trivy JSON, and ZAP JSON. A successful process without readable declared output is `invalid-evidence`. The manifest records suite, component, run, attempt, repository revision, profile digest, normalized findings, and artifact hashes.

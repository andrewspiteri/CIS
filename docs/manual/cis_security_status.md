---
title: "cis security status"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-security-status
---

# `cis security status`

Report one exact or the latest reconciled security run without executing scanners.

```text
cis security status [--run <workflow-run-id>] [--repo <path>] [--format <human|json|agent>]
```

Statuses distinguish passed, passed with accepted/non-blocking findings, failed, incomplete, unavailable, and invalid evidence.

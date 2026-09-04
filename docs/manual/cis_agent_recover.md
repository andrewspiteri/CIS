---
title: "cis agent recover"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-28"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-agent-recover
---

# `cis agent recover`

Recover a non-terminal run whose owned provider process is proven absent.

```text
cis agent recover <run-id> --actor <identity> --reason <rationale> [--repo <path>] [--format <human|json|agent>]
```

CIS refuses recovery while the recorded process identity is active, during the startup grace period, or when the task lock belongs to another run. A valid recovery marks the run `Interrupted`, appends the actor and reason, and removes only its matching stale lock. Use `cis agent resume` afterwards when the provider and retained session support continuation.

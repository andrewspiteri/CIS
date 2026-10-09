---
title: "cis agent recover"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-10-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-agent-recover
---

# `cis agent recover`

Recover a non-terminal run whose owned provider process is proven absent.

```text
cis agent recover <run-id> --actor <identity> --reason <rationale> [--repo <path>] [--format <human|json|agent>]
```

CIS refuses recovery while the recorded process identity is active, during the startup grace period, or when the task lock belongs to another run. A valid recovery marks the run `Interrupted`, appends the actor and reason, and removes only its matching stale lock. Use `cis agent resume` afterwards when the provider supports continuation and the retained worktree, envelope and authority context pass its checks. A reusable native session is not always required: Claude reviews restart with full context because review sessions are nonpersistent. Recovery itself does not approve commands or resume execution.

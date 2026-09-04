---
title: "cis agent cancel"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-28"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-agent-cancel
---

# `cis agent cancel`

Cancel an active foreground run from another process.

```text
cis agent cancel <run-id> --actor <identity> --reason <rationale> [--repo <path>] [--format <human|json|agent>]
```

CIS kills a process tree only when both its process ID and start timestamp match the durable owned identity. A mismatch, missing identity, or terminal run fails closed. Cancellation appends actor, reason, timestamp, and terminal state.

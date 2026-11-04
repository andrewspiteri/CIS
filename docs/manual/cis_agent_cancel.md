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

For an accounted direct task, cancellation preserves spent work and settles its reservation only when owned execution is safely stopped. An unknown or surviving process keeps recovery uncertainty visible. Cancellation does not replenish the containing change or task allowance, clear an engineering hold, or grant another implementation route. Inspect [`cis agent work status`](cis_agent_work.md) and the retained run before continuing.

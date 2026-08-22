---
title: "cis agent prepare"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-14"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-agent-prepare
---

# `cis agent prepare`

Prepare a digest-bound envelope for one task.

```text
cis agent prepare <change-id> <task-id> [--provider portable] [--repo <path>] [--format <human|json|agent>]
```

Blocked tasks and unapproved design gates fail closed. Envelopes live under `.cis/local/agents/`.

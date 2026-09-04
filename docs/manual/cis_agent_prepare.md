---
title: "cis agent prepare"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-28"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-agent-prepare
---

# `cis agent prepare`

Prepare a digest-bound envelope for one task.

```text
cis agent prepare <change-id> <task-id> [--provider portable] [--repo <path>] [--format <human|json|agent>]
```

`--provider` accepts `portable` or a discovered provider ID. Blocked tasks and downstream work behind an unapproved design gate fail closed; coordination, wireframe, and visual-design preparation remain eligible to establish that gate. The derived envelope is written beneath `.cis/local/agents/envelopes/`, binds the canonical task digest, and grants no execution or lifecycle authority.

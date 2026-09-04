---
title: "cis ai route explain"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-ai-route-explain
---

# `cis ai route explain`

Explain canonical route selection, model resolution, remote authorization, and task-class qualification.

```text
cis ai route explain <capability> [--allow-remote] [--repo <path>] [--format <human|json|agent>]
```

Decisions are `selected`, `unqualified`, or `blocked`. An unqualified explanation is truthful output rather than a command failure; missing or invalid repository routes return exit code `4`.

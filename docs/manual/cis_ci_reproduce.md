---
title: "cis ci reproduce"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-ci-reproduce
---

# `cis ci reproduce`

Suggest focused local reproduction commands from failed-job evidence.

```text
cis ci reproduce --run <id> [--provider <kind>] [--repository <owner/repository>]
  [--repo <path>] [--format <human|json|agent>]
```

Suggestions are advisory; prefer the repository-owned CIS workflow that owns the failing suite.

---
title: "cis workflow describe"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-14"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-workflow-describe
---

# `cis workflow describe`

Describe steps and dependencies for one workflow.

```text
cis workflow describe <workflow> [--repo <path>] [--format <human|json|agent>]
```

Read-only; malformed or missing dependencies are reported.

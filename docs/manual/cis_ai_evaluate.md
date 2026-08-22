---
title: "cis ai evaluate"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-14"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-ai-evaluate
---

# `cis ai evaluate`

Run one governed capability route.

```text
cis ai evaluate <capability> --prompt <text> [--allow-remote] [--no-cache] [--repo <path>] [--format <human|json|agent>]
```

Remote use requires both profile permission and explicit authorization. Prompt text is never logged.

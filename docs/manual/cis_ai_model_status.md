---
title: "cis ai model status"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-ai-model-status
---

# `cis ai model status`

Report local qualification evidence and canonical task-class approvals.

```text
cis ai model status [--repo <path>] [--format <human|json|agent>]
```

The command is read-only. Exit code `0` means repository state was readable; `4` means repository resolution failed.

---
title: "cis diagnostics tail"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-14"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-diagnostics-tail
---

# `cis diagnostics tail`

Read the latest bounded lines from one configured source.

```text
cis diagnostics tail <source> [--lines <1-1000>] [--repo <path>] [--format <human|json|agent>]
```

This command returns immediately and does not run a continuous monitor.

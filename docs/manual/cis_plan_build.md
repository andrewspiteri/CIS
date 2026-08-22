---
title: "cis plan build"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-plan-build
---

# `cis plan build`

Builds dependency-aware bounded work from accepted impact findings.

```text
cis plan build <change-id> [--repo <path>] [--format <human|json|agent>]
```

The command blocks if workspace technical intent is not Active/current or impact is not planning-ready. Each work item records complexity,
accepted impact IDs, dependencies, acceptance criteria, validation, and status.
Repeating an identical build returns `unchanged`; an approved plan cannot be silently
rebuilt. Use `cis plan import-spec` when the work must be decomposed from a governed
feature specification.

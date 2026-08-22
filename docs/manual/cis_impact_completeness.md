---
title: "cis impact completeness"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-impact-completeness
---

# `cis impact completeness`

Reports finding counts, covered categories, traversal truncation, review completion,
planning readiness, and explicit gaps.

```text
cis impact completeness <change-id>
  [--repo <path>] [--format <human|json|agent>]
```

Planning readiness requires accepted impact, no proposed or deferred findings, and no
truncated traversal. It is an evidence-coverage assessment, not proof that the graph
contains every semantic impact.

---
title: "cis decision defer"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-decision-defer
---

# `cis decision defer`

Records explicit human deferral and its rationale.

```text
cis decision defer <change-id> <decision-id> --reason <text>
  [--repo <path>] [--format <human|json|agent>]
```

A deferred blocking decision continues to block plan approval. A deferred advisory
decision is reported as a validation warning. Resolved decisions cannot be deferred
silently.

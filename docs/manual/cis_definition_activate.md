---
title: "cis definition activate"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-04"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-definition-activate
---

# `cis definition activate`

```text
cis definition activate --reviewer <human> [--workspace <path>] [--format <human|json|agent>]
```

Records one human decision over the exact complete high-level baseline. It activates the BRD,
technical intent, solution-design bundle, UI direction, diagrams, dictionary index, UI preview,
and high-level backlog, then rebuilds the graph. The fixed reason is recorded by CIS; no separate
rationale option exists.

Every page must first be valid, complete, and current. Canonical files are snapshotted before the
operation and restored if a bounded lifecycle or I/O failure occurs, preventing partial success.

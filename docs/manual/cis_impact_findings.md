---
title: "cis impact findings"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-impact-findings
---

# `cis impact findings`

Lists canonical impact findings, evidence, confidence, rationale, and review state.

```text
cis impact findings <change-id> [--state <state>]
  [--repo <path>] [--format <human|json|agent>]
```

`--state` filters by `proposed`, `accepted`, `rejected`, or `deferred`. The command is
read-only and also reports current completeness.

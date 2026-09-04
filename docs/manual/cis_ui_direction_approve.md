---
title: "cis ui-direction approve"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-03"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-ui-direction-approve
---

# `cis ui-direction approve`

```text
cis ui-direction approve --reviewer <human> --reason <rationale> [--workspace <path>] [--format <human|json|agent>]
```

Approves the exact valid/current high-level UI direction and records reviewer, time, rationale, and
content digest. Agents must not invoke it without explicit human authority. Any later source or
content change makes the approval stale; automation cannot restore Active status.

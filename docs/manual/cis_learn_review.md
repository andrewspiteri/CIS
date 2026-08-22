---
title: "cis learn review"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-14"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-learn-review
---

# `cis learn review`

Record a human approval or rejection of one proposal.

```text
cis learn review <proposal-id> --decision <approve|reject> --reviewer <identity> --reason <rationale> [--repo <path>]
```

A reviewed proposal still cannot change code or acceptance.

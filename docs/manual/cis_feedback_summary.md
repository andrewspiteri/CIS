---
title: "cis feedback summary"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-feedback-summary
---

# `cis feedback summary`

Aggregates the local sanitized tool-usage ledger.

```text
cis feedback summary [--repo <path>] [--since <30m|12h|7d>]
  [--format <human|json|agent>]
```

The result includes invocation outcomes, elapsed time, estimated output tokens,
baseline and actual estimates, possible savings, estimation coverage, and per-command
totals. Commands without a defensible counterfactual contribute zero claimed savings.
Exit `0` includes an empty ledger; exit `2` means invalid input or repository state;
exit `5` means ledger records could not be fully read.

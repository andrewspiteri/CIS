---
title: "cis feedback opportunities"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-feedback-opportunities
---

# `cis feedback opportunities`

Derives deterministic improvement suggestions from local usage history.

```text
cis feedback opportunities [--repo <path>] [--since <30m|12h|7d>]
  [--format <human|json|agent>]
```

The first rules identify commands with at least two failures and commands producing
about 1,000 or more tokens without a registered counterfactual. Suggestions do not
apply fixes or modify canonical files. Repeated repository-aware failures should be
followed by `cis repo doctor`. Exit codes follow `cis feedback summary`.

---
title: "cis feedback opportunities"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-03"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-feedback-opportunities
---

# `cis feedback opportunities`

Derives deterministic improvement suggestions from recent local usage history.

```text
cis feedback opportunities [--repo <path>] [--since <30m|12h|7d>]
  [--format <human|json|agent>]
```

Without `--since`, analysis uses the latest 24 hours. Output compaction is based on
average, p95, and maximum estimated tokens per invocation rather than lifetime totals.
The command also detects adjacent duplicate reads, suppresses its own feedback-reporting
commands, and reports repeated non-success only while the most recent bounded sample is
still unresolved.

Execution failures are distinguished from invalid requests, governed blocks,
cancellation, and Repository Doctor finding-bearing results. Doctor results are not
reported as failed Doctor executions. Suggestions do not apply fixes or modify canonical
files. Use `cis repo doctor` only when repository state or prerequisites plausibly caused
the non-success. Exit codes follow `cis feedback summary`.

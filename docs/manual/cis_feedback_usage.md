---
title: "cis feedback usage"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-03"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-feedback-usage
---

# `cis feedback usage`

Lists recent entries from `.cis/local/feedback/tool-usage.jsonl`.

```text
cis feedback usage [--repo <path>] [--since <30m|12h|7d>]
  [--limit <1-10000>] [--format <human|json|agent>]
```

Entries expose command paths, option names, outcome, timing, output counts, and token
estimates. Option values and command output are never stored. The current reporting
invocation is appended only after its result is produced. Exit codes follow
`cis feedback summary`.

New entries use ledger schema 2 and record a sanitized outcome classification. Schema-1
entries remain readable. Concurrent appends are serialized locally; after the ledger
reaches 16 MiB, disposable history is bounded to 30 days and 25,000 valid entries.

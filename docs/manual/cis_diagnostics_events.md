---
title: "cis diagnostics events"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-14"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-diagnostics-events
---

# `cis diagnostics events`

List bounded normalized diagnostic events.

```text
cis diagnostics events [--source <id>] [--limit <1-5000>]
  [--level <info|warning|error>] [--contains <text>] [--since-minutes <n>]
  [--repo <path>] [--format <human|json|agent>]
```

Known secret-shaped fields are redacted; profile sensitivity remains the primary boundary.
JSONL sources preserve timestamps, categories, correlation identifiers, and normalized
fingerprints. Filters reduce the bounded event set without changing source evidence.

---
title: "cis diagnostics export"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on diagnostics export change"
cis:
  stable_id: change-impact-studio:manual:cis-diagnostics-export
---

# `cis diagnostics export`

Write the bounded, redacted, normalized event set as derived JSONL.

```text
cis diagnostics export [--repo <path>] [--format <human|json|agent>]
```

The output is `.cis/local/diagnostics/export/events.jsonl`; it does not replace raw
runtime or test evidence.

---
title: "cis agent status"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-28"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-agent-status
---

# `cis agent status`

Report provider-neutral envelopes, durable direct runs, and imported result evidence.

```text
cis agent status [--summary] [--repo <path>] [--format <human|json|agent>]
```

Read-only and provider-neutral. Use `cis agent runs` for filters and `cis agent show` for complete attempt provenance.

`--summary` returns a compact JSON projection, including record totals, blocker count, bounded diagnostics and explicit omission counts. It is capped at 12,000 characters and 48,000 UTF-8 bytes. Omit the flag for the complete existing output; omitted records are not successful checks.

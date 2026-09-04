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
cis agent status [--repo <path>] [--format <human|json|agent>]
```

Read-only and provider-neutral. Use `cis agent runs` for filters and `cis agent show` for complete attempt provenance.

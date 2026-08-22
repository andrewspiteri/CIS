---
title: "cis ai cache status"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-14"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-ai-cache-status
---

# `cis ai cache status`

Report disposable model-response cache entry count.

```text
cis ai cache status [--repo <path>] [--format <human|json|agent>]
```

Cache lives under `.cis/local/ai/cache/` and is non-authoritative.

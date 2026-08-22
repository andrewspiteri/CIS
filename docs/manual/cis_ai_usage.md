---
title: "cis ai usage"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-14"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-ai-usage
---

# `cis ai usage`

List sanitized model-use records without prompts or responses.

```text
cis ai usage [--limit <1-1000>] [--repo <path>] [--format <human|json|agent>]
```

Records contain hashes, token estimates, duration, locality, cache state, and outcome.

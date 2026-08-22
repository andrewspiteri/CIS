---
title: "cis ai status"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-ai-status
---

# `cis ai status`

Reports provider availability, locality, endpoint, and model names without submitting
repository content or invoking generation.

```text
cis ai status [--format <human|json|agent>]
```

Local Ollama is read from `OLLAMA_HOST`. The optional `openai-compatible` provider is
configured through `CIS_AI_ENDPOINT`, `CIS_AI_MODEL`, and `CIS_AI_API_KEY`. Reporting a
remote provider as available does not authorize its use. Exit `0` means status was
reported; exit `2` means the output format is invalid.

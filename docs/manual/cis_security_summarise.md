---
title: "cis security summarise"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-security-summarise
---

# `cis security summarise`

Create a deterministic report and optionally append advisory triage from a local model.

```text
cis security summarise --run <workflow-run-id> [--no-llm] [--repo <path>] [--format <human|json|agent>]
```

Only normalized redacted findings enter the prompt. Remote providers are disabled for this command. Summary metadata proves local-only routing and deterministic-verdict preservation. `--no-llm` always remains available.

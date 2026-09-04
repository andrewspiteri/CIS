---
title: "cis agent providers"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-03"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-agent-providers
---

# `cis agent providers`

List the portable envelope provider and every loaded direct-execution provider with its transports, modes, permission ceilings, resumption support, and interaction capabilities.

```text
cis agent providers [--repo <path>] [--format <human|json|agent>]
```

This command performs no provider authentication or model request. Use `cis agent provider diagnose` for an executable and authentication probe. Duplicate provider IDs fail closed.
Structured output contains only provider discovery evidence and diagnostics; unrelated
retained runs and imports are not hydrated or serialized.

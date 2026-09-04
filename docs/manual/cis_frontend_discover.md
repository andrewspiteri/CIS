---
title: "cis frontend discover"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on command or provider change"
cis:
  stable_id: change-impact-studio:manual:cis-frontend-discover
---

# `cis frontend discover`

Discover normalized web, native, and Godot frontend context.

```text
cis frontend discover [--repo <path>] [--format <human|json|agent>]
```

Derived state is written to `.cis/local/frontend/context.json`. The command is idempotent for unchanged sources and providers. Exit code `0` means discovery completed without errors; `5` means a deterministic provider or route error exists.

---
title: "cis generate render"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-14"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-generate-render
---

# `cis generate render`

Render one template with explicit values.

```text
cis generate render <template> --output <repo-relative-path> [--set <key=value>...] [--repo <path>] [--format <human|json|agent>]
```

Unresolved variables and paths outside the repository fail closed. Render evidence is disposable.

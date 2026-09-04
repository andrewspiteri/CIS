---
title: "cis references validate"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-references-validate
---

# `cis references validate`

Validate provider conflicts, canonical identities, source correlation, and evidence paths.

```text
cis references validate [--strict] [--no-refresh]
  [--repo <path>] [--format <human|json|agent>]
```

Without `--no-refresh`, source discovery runs first. Strict mode makes every unresolved
warning a failing governance result.

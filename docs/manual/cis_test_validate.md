---
title: "cis test validate"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-test-validate
---

# `cis test validate`

Validate suite identities, layers, adapters, commands, working directories, and evidence paths.

```text
cis test validate [--strict] [--repo <path>] [--format <human|json|agent>]
```

Strict mode requires result, coverage, and mutation evidence beneath `.cis/local/`.

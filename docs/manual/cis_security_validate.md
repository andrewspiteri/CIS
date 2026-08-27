---
title: "cis security validate"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-security-validate
---

# `cis security validate`

Validate suite IDs, categories, registered result adapters, commands, working directories, and safe result paths.

```text
cis security validate [--strict] [--repo <path>] [--format <human|json|agent>]
```

Strict mode requires result evidence paths beneath `.cis/local/security/` and the canonical accepted-findings registry.

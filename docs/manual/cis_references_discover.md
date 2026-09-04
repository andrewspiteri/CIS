---
title: "cis references discover"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-references-discover
---

# `cis references discover`

Extract deterministic source identities and correlate them with canonical reference tables.

```text
cis references discover [--repo <path>] [--format <human|json|agent>]
```

The idempotent normalized result is written beneath `.cis/local/references/`. Canonical
Markdown is never changed.

---
title: "cis references reconcile"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-references-reconcile
---

# `cis references reconcile`

Preview or apply safe additive rows from deterministic source discovery to selected canonical references.

```text
cis references reconcile [--kind <kind>] [--yes]
  [--repo <path>] [--format <human|json|agent>]
```

Only configuration dictionaries, module ownership maps, and package catalogues are supported.
Without `--yes`, the command reports the exact additions and exits without changing canonical
Markdown. With `--yes`, it appends missing source-backed rows without rewriting or removing
human-managed rows. Reference families that require semantic judgement remain manual.

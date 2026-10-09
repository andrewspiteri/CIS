---
title: "cis references reconcile"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-10-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-references-reconcile
---

# `cis references reconcile`

For tables with a `Repository` column, additive rows retain that column and name the
selected repository. Existing participant rows remain unchanged.

Preview or apply safe additive rows from deterministic source discovery to selected canonical references.

```text
cis references reconcile [--kind <kind>] [--yes]
  [--repo <path>] [--format <human|json|agent>]
```

Configuration dictionaries, module ownership maps, package catalogues and screen-route maps are supported.
Use repeated `--kind` options to select families with existing canonical tables. If discovery finds
rows for a missing table, the current implementation can fail before producing a preview;
select existing families explicitly and review the missing family separately.
Without `--yes`, the command reports the exact additions and exits without changing canonical
Markdown. With `--yes`, it appends missing source-backed rows without rewriting or removing
human-managed rows. Reference families that require semantic judgement remain manual.

Review proposed descriptions, sensitivity and lifecycle as well as identities before retaining
additions. Discovery is not semantic approval. In particular, configuration discovery currently
labels JSON settings with environment-style locators; correct their source paths and descriptions
when reconciling manually. Do not adopt an inaccurate setting definition solely to clear a
discovery warning.

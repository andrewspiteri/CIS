---
title: "cis technical-intent init"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-20"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-technical-intent-init
---

# `cis technical-intent init`

Initializes or reconciles the authority repository's workspace-scoped technical intent
against the Active BRD semantic digest and exact participant graph builds.

```text
cis technical-intent init [--workspace <path>] [--format <human|json|agent>]
```

The command requires exactly one workspace authority, an Active/current BRD, and fresh
participant graphs. It preserves human-authored sections, adds missing lifecycle metadata,
and owns only the marked baseline block. Managed BRD provenance changes do not alter the
semantic digest. A legacy full-file BRD baseline is migrated once without clearing a current
technical-intent approval. Later semantic BRD changes or a change to the registered participant
set reset the document and catalog to Draft. A participant graph build-version refresh updates
managed provenance but preserves a current approval because it does not, by itself, change the
approved technical direction. Run workspace graph build and strict validation afterward.
Exit `0` means initialized or unchanged; repository/workspace structural errors exit `2`;
and unmet BRD or participant-graph readiness exits `5` as a workflow block.

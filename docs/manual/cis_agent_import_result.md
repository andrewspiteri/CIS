---
title: "cis agent import-result"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-28"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-agent-import-result
---

# `cis agent import-result`

Import a structured result against an unchanged envelope.

```text
cis agent import-result <envelope> --result <json-file> [--repo <path>] [--format <human|json|agent>]
```

Stale envelopes and unsafe paths are rejected. Import appends evidence but never completes or approves a task.

The result must use schema version 1, match the exact envelope identity, contain a non-empty summary, and provide safe changed-file paths. A direct run does not import itself automatically; inspect `cis agent show` and import its `result.json` explicitly when the evidence is useful.

`changedFiles` entries are repository-relative by default. An authority-owned task that changes a registered participant may instead use `<repository-id>::<relative-path>`, for example `todo-api::src/app.ts`. The repository ID must exist in the authority's `.cis/workspace.yml`; absolute paths, traversal, empty segments, unknown repositories, and nested qualifiers are rejected.

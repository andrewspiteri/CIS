---
title: "cis technical-intent refresh"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-23"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-technical-intent-refresh
---

# `cis technical-intent refresh`

Refreshes the approved BRD, technical-intent, and high-level-backlog baseline chain
after repository implementation graphs change.

```text
cis technical-intent refresh [--workspace <path>] [--format <human|json|agent>]
```

The command runs safe BRD reconciliation, technical-intent baseline initialization,
and high-level-backlog regeneration in order. Each canonical service preserves its
existing reviewer, rationale, and approval only when semantic product or technical
authority is unchanged and all discovered source evidence remains resolved. Managed
graph IDs and provenance are refreshed without asking a human to approve the same
content again.

The operation stops at the first material change: new or changed unassessed business
evidence, edited approved content, unresolved technical decisions, changed backlog
outcomes/routing/dependencies, or invalid documents. It reports the affected stage so
the human reviews only that difference. It never grants or invents approval.

Run this command after implementation graph rebuilds and before starting the next
high-level backlog feature. Exit `0` means all three authorities are Active and current.
Exit `5` means a material governance review is required.

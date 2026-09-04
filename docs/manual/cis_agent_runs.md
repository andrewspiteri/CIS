---
title: "cis agent runs"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-03"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-agent-runs
---

# `cis agent runs`

List durable local run manifests.

```text
cis agent runs [--change <change-id>] [--task <task-id>] [--status <state>] [--summary] [--limit <1-100>] [--latest-per-task] [--repo <path>] [--format <human|json|agent>]
```

Filters are optional and read-only. Manifests identify the provider, transport, target repository, exact revision and working-tree digest, isolation path, permission ceiling, actor, attempt, provider session, and terminal state.
Structured output contains only the selected manifests and diagnostics; provider and import
inventories are not repeated.

`--summary` retains the bounded list and freshness fields needed for routing: IDs, task
digest, provider, update/completion timestamps, status, and failure kind. It returns at most
10 rows by default; `--limit` accepts 1–100. `--latest-per-task` keeps only the newest run for
each change/task pair. Full permission, transport, actor, process, and artifact provenance
remains available through `cis agent show`.

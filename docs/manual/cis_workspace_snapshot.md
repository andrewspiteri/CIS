---
title: "cis workspace snapshot"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-14"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-workspace-snapshot
---

# `cis workspace snapshot`

Reads the common workspace startup projections in one process. Graph input hashes,
classification, and nested status reads share one bounded read scope, so each checked
input is reused across the projections that need it.

## Synopsis

```text
cis workspace snapshot [--repo <authority-path>] [--format <human|json|agent>]
```

`--repo` defaults to the current directory; `--format` defaults to `human`.
The command accepts no arbitrary commands or mutation options. It runs a fixed set
of 15 reads: feature navigation, BRD status and questions, technical-intent status and question summary,
change list, agent providers and run summaries, Doctor, skills and standards inventories,
reference validation, definition status, and business-question guidance. Details for
a specific agent run are requested separately when displayed.

## Results and freshness

JSON schema version 1 contains `repositoryPath`, `checkedAt`, `durationMs`, and `entries`.
Each entry identifies the original `arguments` and scope option (`--repo` or
`--workspace`), with its unchanged JSON `data`, `exitCode`, `standardError`, and
`durationMs`. An unavailable or malformed child response has null `data`.
Human and agent formats summarize the per-query timings and exit codes.

The snapshot releases its read scope at the end of the request. A new invocation
checks source content again, including edits that preserve file size and timestamp.
It is a bounded read view, not a filesystem transaction: external edits may occur
during an invocation. If watched inputs change while a snapshot is loading, the VS Code
client discards it and retries once. Concurrent readers share the replacement snapshot.
Repeated changes leave a visible freshness error; switching authority or executable
also rejects the pending result. Mutations and explicit refresh discard cached results.
Only the fixed startup queries use this snapshot. Filtered run lists and individual run
details execute directly, without first loading unrelated workspace projections.
Canonical documents and approval state are not changed. Normal disposable local
status caches and the invocation's tool-usage record may be updated.

## Exit codes

| Code | Meaning |
| ---: | --- |
| `0` | The aggregate completed; inspect every entry's exit code for readiness or failure. |
| `2` | Arguments or output format are invalid. |
| `1` | The aggregate could not complete. |

Doctor errors, blocked governance states, and missing optional modules remain visible
as individual results. Aggregate success does not mean the workspace is ready.

## Related commands

- [`cis repo doctor`](cis_repo_doctor.md)
- [`cis definition status`](cis_definition_status.md)
- [`cis graph status`](cis_graph_status.md)

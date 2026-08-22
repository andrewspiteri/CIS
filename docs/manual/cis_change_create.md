---
title: "cis change create"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-change-create
---

# `cis change create`

Creates a catalogued `changes/CIS-NNNN/` dossier against the current exact Git commit
or graph-build baseline.

```text
cis change create --title <text> --outcome <text>
  [--id CIS-0001] [--root <node-id>[#<kind>]]... [--repo <path>]
  [--format <human|json|agent>]
```

Each `--root` becomes an initial impact-analysis root. The command requires an
initialized repository and built graph, refuses an existing or invalid ID, writes
`proposal.md`, `impact.md`, `decisions.md`, `plan.md`, `design.md`, `verification.md`,
an `agent-tasks/` directory, and `events.jsonl`. The Markdown records are added to
`catalog.yml`. `design.md` is the explicit UI-approval ledger; `verification.md` is
the cross-task execution-evidence ledger. In a workspace authority, creation also requires
an Active, current technical intent; use `cis technical-intent status` to diagnose the
gate. Exit `0` means created; exit `2` means
invalid repository, graph, input, or collision.

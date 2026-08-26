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

Creates a catalogued `changes/CIS-NNNN/` dossier against the current exact Git commit,
dirty working-tree state, or graph-build baseline.

```text
cis change create --title <text> --outcome <text>
  [--id CIS-0001] [--root <node-id>[#<kind>]]... [--repo <path>]
  [--format <human|json|agent>]
```

Each `--root` becomes an initial impact-analysis root. The command requires an
initialized repository and built graph, refuses an existing or invalid ID, writes
`proposal.md`, `impact.md`, `decisions.md`, `plan.md`, `wireframes.md`, `design.md`,
`test-cases.md`, `test-cases.csv`, `verification.md`, an `agent-tasks/` directory,
and `events.jsonl`. The Markdown records are added to
`catalog.yml`. `design.md` is the explicit UI-approval ledger; `verification.md` is
the cross-task execution-evidence ledger. The initially empty manual-test files are
populated together when a feature specification is imported or derived. In a workspace authority, creation also requires
an Active, current technical intent; use `cis technical-intent status` to diagnose the
gate. Exit `0` means created; exit `2` means
invalid repository, graph, input, or collision.

The initial proposal contains an outcome-acceptance placeholder. A human may replace it
for an ad-hoc change. For a current approved feature, `cis plan derive` replaces only that
placeholder with the feature's exact approved requirement criteria and preserves any
existing human-managed proposal criteria.

For every Git-backed workspace repository, the proposal records the starting commit and
content digests for tracked changes and untracked files already present at creation.
This does not copy source content into the dossier. It gives `cis verify diff` an exact
change-start boundary even when one or more repositories are intentionally dirty.

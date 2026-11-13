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
initialized repository and built graph and refuses an existing or invalid ID.
It creates the draft `proposal.md`, compact `creation-inputs.json` and `events.jsonl`; only actual Markdown records
are catalogued. Preparing this draft requires no preliminary human approval and
grants no execution or product-baseline approval. Exit `0` means created; exit `2`
means invalid repository, graph, input, or collision.

Other records appear when their subjects apply: an actual impact analysis, bounded
plan/tasks, decision, verification evidence, UI design or manual-test obligation.
Accepted no-UI work creates no visual templates. Historical full dossiers remain
readable without deleting or migrating their records.

The initial proposal contains an outcome-acceptance placeholder. A human may replace it
for an ad-hoc change. For a current approved feature, `cis plan derive` replaces only that
placeholder with the feature's exact approved requirement criteria and preserves any
existing human-managed proposal criteria.

For every Git-backed workspace repository, `creation-inputs.json` records the starting commit and
content digests for tracked changes and untracked files already present at creation.
The proposal binds that machine record by path and SHA-256 instead of printing its inventory.
This does not copy source content into the dossier. It gives `cis verify diff` an exact
change-start boundary even when one or more repositories are intentionally dirty.

## Authorize the outcome once

Reuse an applicable existing authorization. Otherwise prepare the complete outcome,
explicit scope, constraints and finite overall limits, then record the actual human
decision:

```text
cis change authorize <change-id> --input <request.json>
  --reviewer <human> --reason <rationale> [--repo <path>]
  [--format <human|json|agent>]
```

The JSON request supplies `outcome`, `scope` entries with `id`, `description` and path
prefixes, `constraints`, overall `seconds`, `attempts`, `restorationSeconds`, finite
`storage`, delegated `controllers`, `organizeTasks`, `distributeResources`, nullable
`visualUi`, `manualTestCatalogue` and the actual `decisionReference`.
The shared `authorization.json` retains the original reviewer, reason and decision;
derived records reference it instead of claiming another human review.

Authorized controllers may organize supporting work and distribute resources within
those limits through the [planning](cis_plan_build.md) and [work](cis_agent_work.md)
commands. Task count or a refreshed plan hash does not require another approval.
Material changes outside scope or limits need their specific new decision reference;
legacy exact-task and exact-plan approvals keep their original boundaries.

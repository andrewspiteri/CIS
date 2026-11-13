---
title: "cis plan build"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-11-10"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-plan-build
---

# `cis plan build`

Prepares bounded work and derives executable task records under applicable outcome
authority, or uses the existing accepted-impact planning path.

```text
cis plan build <change-id> [--repo <path>] [--format <human|json|agent>]
```

Draft preparation needs no preliminary human approval. With a current outcome
authorization, the plan and tasks derive from its explicit scope and retain the
original human decision. Each work item records complexity, scope references,
dependencies, task-specific acceptance, validation and status.
It also writes the actual `agent-tasks/WORK-*.md` contracts and their catalogue links.
This ad hoc route needs no feature specification or unrelated feature workstreams.
Preparation grants no execution authority: applicable product readiness, task
eligibility, dependencies and finite shared work/storage limits still govern admission.

## Organize authorized tasks

```text
cis plan task add <change-id> --input <request.json> --actor <controller> --reason <rationale>
cis plan task split <change-id> <task-id> --input <request.json> --actor <controller> --reason <rationale>
cis plan task update <change-id> <task-id> --input <request.json> --actor <controller> --reason <rationale>
```

These operations require delegated task organization under the current authorization.
The input is a JSON array or an object with `tasks`. Each definition supplies
`title`, `scopeIds`, `acceptanceCriteria` and `validation`; optional `paths`,
`dependsOn`, `complexity`, `category` and `assignee` refine that task.
Bind each task to explicit authorized scope IDs; add only its specific work,
acceptance, validation and dependencies. Supporting tests, documentation and in-scope
fixes use the same authority. Splitting unfinished work preserves its original
identity/history and costs. Task organization grants no new capacity; use the existing
[allocation command](cis_agent_work.md) within the unchanged shared total.

Authorized refresh, ordering and decomposition do not invalidate authority because a
generated plan hash changed. A material scope, contract or overall-limit difference
requires a targeted decision. Existing completed costs/results remain intact, and
unrelated work stays eligible where dependencies and shared resources permit.

## Retained accepted-impact plans

Legacy plans retain their existing reviewed impact and exact-plan boundaries. Their
original approval does not grant general outcome delegation. The following identity
and reconciliation rules still apply to that route.

Task identity follows the accepted impact identity, not its current sort position.
Adding or reordering findings does not transfer an existing task or accounting owner.
Removed work is explicitly archived under `agent-tasks/retired/`, with catalogue and
event history; its identifier stays reserved. The same unchanged impact can reactivate
its original task and authored evidence in Draft status. Reactivation grants no fresh
capacity or completion approval. Legacy rows without a retained durable document/input
basis remain held: their ID and title do not prove the historical target or evidence.
Their recorded identities and row contracts are preserved. This also applies to approved
legacy plans; the approved-plan rebuild restriction remains in force, and a row alone
does not establish an executable canonical task owner. Restore verified retained
contracts/bindings or use the existing governed change/impact route to reconcile them.

An unchanged retained task document is preserved byte-for-byte, including authored
objectives, required changes, scope, acceptance, validation and evidence. If an accepted
impact's category, target, label or evidence changes, build returns
`reconciliation-required` before replacing its contract. Validation and approval enforce
the same hold independently, even if build is skipped. Restore verified accepted inputs
or retained binding when corrupted; use the existing governed change/impact route for
genuinely different scope. This command does not amend an established contract by
stamping a new hash, and introduces no automatic scope-amendment operation. Missing,
unsafe or invalid durable documents block validation and approval rather than being
silently regenerated.

If accepted work adds a prerequisite to a retained task, or removes one of its recorded
dependencies, build holds before changing the authored contract. Restore verified
accepted inputs when corrupted, or reconcile the required ordering and retained
contract through the existing governed change/impact route before rebuilding.

Task, plan, catalogue and event writes use the existing separate files. An I/O failure
can leave a partially applied build; no cross-file rollback is promised. Preserve those
files and restore a verified consistent contract/plan/catalogue binding before retrying.
Do not delete a retained identity or replace an input hash to bypass that hold.

Draft rebuilds retain authored plan paragraphs and sections. When accepted work changes,
the command refreshes its generated task table, execution manifest and derived summary
fields. A changed rebuild holds for reconciliation if it cannot identify the existing
projections unambiguously.
Repeating an identical build preserves the existing plan bytes and returns `unchanged`
without document/catalogue/event churn; an approved legacy exact plan cannot be silently rebuilt.
Use `cis plan import-spec` when the work
must be decomposed from a governed feature specification; that route retains its existing
feature coverage and approval rules.

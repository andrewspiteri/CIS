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

Builds dependency-aware bounded work from accepted impact findings.

```text
cis plan build <change-id> [--repo <path>] [--format <human|json|agent>]
```

The command blocks if workspace technical intent is not Active/current or impact is not planning-ready. Each work item records complexity,
accepted impact IDs, dependencies, acceptance criteria, validation, and status.
It also writes the actual `agent-tasks/WORK-*.md` contracts and their catalogue links.
This ad hoc route needs no feature specification or unrelated feature workstreams.
The documents are drafts: ordinary source/impact/plan approval, task eligibility and
applicable finite work/storage authority still govern execution.

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
without document/catalogue/event churn; an approved plan cannot be silently rebuilt.
Use `cis plan import-spec` when the work
must be decomposed from a governed feature specification; that route retains its existing
feature coverage and approval rules.

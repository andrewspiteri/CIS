---
title: "What Is a Change Dossier?"
type: article
status: Active
series: "Change Impact and Planning"
series_order: 1
owner: "Andrew Spiteri"
last_reviewed: "2026-09-21"
review_cadence: on change-lifecycle change
summary: "A durable repository record that connects one outcome to impact, decisions, work, evidence, and acceptance."
cis:
  stable_id: change-impact-studio:article:what-is-a-change-dossier
---

# What is a change dossier?

Chats, tickets, and pull requests each record part of a software change. None reliably
holds its full engineering meaning.

A change dossier is the repository-owned record that connects one proposed outcome to
its baseline, reviewed impact, decisions, bounded work, design, tests, verification, and
acceptance.

In a product workspace, the dossier begins only after product-wide authority is current.
For feature work, intake and feature-definition review normally come first, followed by
the governed backlog and feature-specification gates. The feature request explains the
proposed product change; the dossier governs the approved delivery outcome, impact,
work, and evidence. Neither document replaces the product definition.

A bounded engineering change that does not originate in a feature BRD can still create a
dossier directly. The distinction is the source of authority, not the importance of the
change.

## One home for the change

CIS stores each dossier beneath the configured documentation root:

```text
changes/CIS-0001/
├── proposal.md
├── impact.md
├── decisions.md
├── plan.md
├── wireframes.md
├── design.md
├── test-cases.md
├── test-cases.csv
├── verification.md
├── agent-tasks/
└── events.jsonl
```

The records have different responsibilities. `proposal.md` establishes the outcome and
baseline. `impact.md` preserves findings and dispositions. `decisions.md` keeps options
and rationale. `plan.md` routes bounded work. Task documents hold complete execution
contracts. `verification.md` records planned-versus-actual evidence.

## The dossier is not a ticket

A ticket usually optimizes coordination: priority, assignment, status, and discussion.
The dossier optimizes engineering authority and traceability. External issues may
project its work, but changing or closing an issue cannot silently redefine the dossier.

This distinction lets teams continue using their preferred tracker while keeping the
durable task contract beside the code and specifications it governs.

## The dossier is catalogued

Every dossier document has stable identity, path, type, lifecycle, and authority in the
documentation catalog. Graph queries can relate the change to requirements, components,
contracts, tests, and decisions without making the graph the source of truth.

Stable identities also preserve history through re-analysis and task migration.

## Managed files do not invalidate their own baseline

Creating a dossier changes repository files. If those managed records participated in
the product graph identity, the act of recording a baseline would immediately make the
baseline stale.

CIS excludes managed dossier inputs from product/source graph identity while still
allowing them to appear as graph content. Product source, specifications, references,
and other governed inputs remain freshness checked.

## Events preserve lifecycle history

Disposition, decisions, approval, task transitions, and acceptance append events. The
current Markdown remains readable, while the event ledger records how the state changed.

The ledger does not grant authority on its own. It supports the canonical state and
helps detect silent replacement.

## The dossier follows a lifecycle

A useful dossier changes character as work progresses:

```text
Proposed outcome and exact baseline
  → evidence-backed impact findings
  → human dispositions and decisions
  → approved bounded work
  → candidate implementation and execution claims
  → independent verification
  → human acceptance or recorded residual work
```

Later stages do not erase earlier ones. A rejected impact remains part of the reasoning.
A superseded decision remains part of history. An unexpected file found during
verification can be compared with the plan that genuinely existed before execution.

The directory is not valuable because it contains many files. It is valuable because
each file owns one part of the authority chain. A reader can move from the requested
outcome to the evidence used for acceptance without reconstructing the change from a
conversation timeline.

## A dossier is intentionally bounded

One dossier should represent one observable product or engineering outcome. It should not
become a second backlog, a product strategy document, or a container for every nearby
cleanup. If work reveals another outcome with separate authority or risk, create or link
a separate change instead of stretching the original proposal until its baseline and
acceptance criteria lose meaning.

The boundary is particularly important across repositories. A workspace dossier may
coordinate several product-owned repositories, but a required modification in a
dependency repository belongs to that dependency's product authority. The current dossier
records the impact and coordination obligation; it does not acquire permission to deliver
the other product's change.

The tracker can still answer “who is working on this?” and “which sprint contains it?”
The dossier answers “which authority approved this scope?”, “what was the baseline?”,
“which evidence is required?”, and “what changed when the implementation disagreed with
the plan?” Those questions need repository history and stable identities, not only a
mutable issue description.

## Walk through a small dossier

For “allow a list owner to revoke an invitation,” the proposal identifies the observable
revocation outcome and current graph build. Impact finds the customer screen, API
operation, permission, invitation state, audit event, and cleanup behavior. Review accepts
five findings, rejects an unrelated sign-in route, and defers bulk revocation.

A decision chooses whether revocation invalidates already opened links immediately. The
plan then creates contract, backend, customer UI, observability, test, and verification
work with explicit dependencies. During delivery, Git reveals an unexpected workflow
change needed to retain integration-test output. The reviewer either revises scope with
evidence or removes the workflow edit. Verification records the final diff, checks,
deferral, and residual risk before a human accepts the result.

Every important boundary is visible in the dossier. The pull request can link to it; the
pull request does not have to contain it all.

## Keep durable conclusions, not every byte

Raw provider transcripts, large CI logs, caches, temporary worktrees, and repeated query
results belong in derived local or external systems. The dossier records bounded evidence
references, hashes, outcomes, and unavailable checks. It should be durable enough to
explain the change without becoming an archive of every byte produced during delivery.

The managed-file baseline exclusion is equally narrow. It prevents the dossier's own
generated records from creating a freshness loop; it does not exempt a feature
specification, reference dictionary, or technical-intent document merely because the
change links to it. Sources that establish product meaning must still match the approved
baseline.

## Takeaway

A change dossier turns transient delivery conversation into durable project state. It
keeps the outcome, authority, work, and evidence together without replacing trackers,
Git, or CI/CD.

## Canonical CIS sources

- [Change impact and bounded planning](../specs/change-impact-and-planning-spec.md)
- [`cis change create`](../manual/cis_change_create.md)
- [Context model and local graph](../specs/context-model-and-graph-spec.md)
- [High-level product-definition wizard](../specs/high-level-product-definition-wizard-spec.md)
- [`cis brd feature intake`](../manual/cis_brd_feature_intake.md)

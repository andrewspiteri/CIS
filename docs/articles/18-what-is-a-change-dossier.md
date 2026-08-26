---
title: "What Is a Change Dossier?"
type: article
status: Draft
series: "Change Impact and Planning"
series_order: 1
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
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

## Takeaway

A change dossier turns transient delivery conversation into durable project state. It
keeps the outcome, authority, work, and evidence together without replacing trackers,
Git, or CI/CD.

## Canonical CIS sources

- [Change impact and bounded planning](../specs/change-impact-and-planning-spec.md)
- [`cis change create`](../manual/cis_change_create.md)
- [Context model and local graph](../specs/context-model-and-graph-spec.md)


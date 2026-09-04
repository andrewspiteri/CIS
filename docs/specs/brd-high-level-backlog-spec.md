---
title: "BRD High-Level Backlog"
type: specification
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-23"
review_cadence: "on BRD decomposition workflow change"
cis:
  stable_id: change-impact-studio:spec:brd-high-level-backlog
---

# BRD high-level backlog

## Purpose

The high-level backlog is the reviewed bridge between approved product/technical
authority and feature delivery. It decomposes each functional BRD requirement into
one stable product outcome without prematurely generating implementation tasks.

The sequence is:

```text
Active BRD -> Active technical intent -> Active overall solution-design bundle
-> approved high-level backlog
-> feature specification -> change dossier/impact -> bounded task plan
```

## Canonical artifact

`cis brd backlog build` creates
`<authority-documentation-root>/plans/high-level-backlog.md`. Markdown and its catalog
entry are canonical; derived graph state remains disposable.

Every `HLT-*` row records exactly one functional BRD requirement, outcome, priority,
affected repositories, public/customer/backoffice frontend types, high-level
dependencies, feature-specification path, and human notes. Quality requirements and
success measures remain visible as global acceptance obligations until applicable
feature specifications and final assurance allocate them.

Initial dependency suggestions use bounded capability rules for infrastructure,
authentication, aggregate creation, and shared-link consumption. Repository routing uses
the initialized repository classifications rather than repository-name conventions, and
token matching is word-aware so terms such as `possession` do not imply session scope.

## Lifecycle and gates

Build requires an Active/current BRD, Active/current technical intent, and Active/current
overall solution design plus component sheet. The backlog
records hashes of both sources. Validation requires complete one-to-one functional
requirement coverage, known acyclic dependencies, affected repositories, bounded
frontend classifications, and no placeholders.

Build or source change produces `Review Required`. Explicit `cis brd backlog approve`
records reviewer, rationale, timestamp, and approved-content digest. Source drift or a
later canonical backlog edit makes an Active backlog effectively `Stale`.

Approval authorizes creation of feature specifications. It does not authorize detailed
tasks, implementation, design, deployment, or release.

`cis brd backlog start --item <HLT-ID>` performs the governed transition for one
dependency-ready item. It creates and catalogs a Draft authority-owned feature
specification, records exact source provenance, and updates the item's managed feature
link without changing the approved outcome or dependency authority. Draft specifications
created from a backlog item remain downstream work-in-progress and do not enter BRD
absorption until their lifecycle advances beyond Draft.

The start operation deliberately creates a governed schema scaffold. It is not presented
as a finished specification. `cis agent author feature --item <HLT-ID>` may expand that
single file from the current approved BRD, technical intent, solution design, component
sheet, UI direction, and applicable governance in an isolated scratch repository. Apply
requires unchanged frontmatter, an exact one-file diff, all required sections, structured
feature requirements, and no template placeholders. The result retains Draft authority.

`cis brd feature validate/status/approve --item <HLT-ID>` governs that transition.
Validation requires complete sections, structured requirements, bounded surfaces and
frontend types, exact repository routing, and no placeholders. The source digest is
scoped to the stable backlog item so unrelated feature-link updates do not revoke review.
Explicit feature approval records human authority and a content digest. The resulting
Active feature becomes eligible BRD evidence; it must be reconciled and assessed before
downstream change planning. Feature approval does not directly authorize implementation,
deployment, or release. CIS may reuse its exact reviewer, rationale, path, and content
digest to atomically adopt eligible deterministic impacts and approve the resulting
bounded plan through `cis plan derive`; any uncertainty or scope expansion requires a
new human decision before implementation.
An unapproved feature cannot report Ready for Approval while its backlog is stale. An
already Active feature retains its own item-scoped authority during the intentional BRD
absorption and downstream renewal cycle.

## Reconciliation

Rerunning build is idempotent. Stable IDs derive from BRD requirement IDs. CIS refreshes
derived outcome, priority, repository, and frontend routing while preserving reviewed
dependencies, feature-specification links, and notes for unchanged requirement IDs.
Schema migration re-derives dependencies in older, unapproved generated rows once; it
never replaces dependencies in an Active backlog. Schema 3 then preserves dependency
lists, including an explicitly empty list, as human-managed decisions.

After implementation graph rebuilds, `cis technical-intent refresh` safely reconciles
the BRD, technical-intent baselines, and backlog. When only managed graph/provenance
identities changed, all existing approvals remain attributed to their original human
reviewers and no renewal prompt is required. New evidence or changed product, technical,
or backlog semantics stops the refresh at the affected authority.

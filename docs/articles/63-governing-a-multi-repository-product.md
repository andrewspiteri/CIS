---
title: "Governing a Multi-Repository Product"
type: article
status: Active
series: "CIS in Practice"
series_order: 3
owner: "Andrew Spiteri"
last_reviewed: "2026-09-10"
review_cadence: on workspace-governance change
summary: "Establish one product authority while distinguishing owned repositories from producer and consumer dependencies."
cis:
  stable_id: change-impact-studio:article:govern-multi-repository-product
---

# Governing a multi-repository product

A product spanning web, services, mobile, and infrastructure needs shared intent without
turning one repository into a stale copy of all the others.

## Initialize the authority

```powershell
cis workspace init --root docs --ecosystem commerce --product ordering --dry-run
cis workspace init --root docs --ecosystem commerce --product ordering --yes
```

The authority owns the product's canonical business requirements, workspace-scoped
technical intent, product-definition baseline, and cross-repository change dossiers. Its
ecosystem and product IDs are stable governance identities, not display-only labels.

## Import participants

```powershell
cis repo import `
  --workspace C:\work\product-docs `
  --source C:\work\web C:\work\api C:\work\infra `
  --root docs\cis `
  --participation owned --relationship none `
  --dry-run
```

Import plans the whole batch before changing the first repository, initializes the sources,
and atomically registers their locations. It does not copy, clone, move, or execute source.
A repository owned
by another product is imported separately as a `dependency` with an explicit `producer`,
`consumer`, or `bidirectional` relationship and optional component scope.

## Build and validate independently

```powershell
cis graph build --workspace C:\work\product-docs
cis graph validate --workspace C:\work\product-docs --strict
```

Each repository retains its own status and graph identity. Partial failure remains visible.

## Govern shared intent

Business requirements assess product-owned source documents and baselines. Technical
intent establishes ownership, trust, integration, data, and operational direction while
recording dependency surfaces without absorbing their implementation choices. Material
owned-product drift blocks new governed workspace work until reviewed.

The product-definition journey consolidates only owned-product evidence into the authority's
business, technical, architecture, dictionary, experience, and backlog artifacts.
Dependencies remain bounded integration context. Modifying one requires a change governed
by its own product workspace.

## Design the workspace topology first

List the repositories that define and implement the product, the repository that will
hold product-wide authority, and the external producers or consumers needed for context.
For every entry, record:

- stable repository identity and documentation root;
- authority or participant role;
- owned or dependency participation;
- dependency direction where applicable;
- component scope for broad dependencies; and
- expected source, contract, test, and delivery responsibilities.

This review prevents a convenient checkout layout from deciding product ownership.

## Import in coherent batches

Owned API, web, mobile, and infrastructure repositories can be imported together when
they share the intended documentation root. Preview the complete batch before confirming;
one invalid source or collision should block mutation rather than leave a partial
registry.

Import dependencies separately with explicit producer, consumer, or bidirectional
relationships and optional components. Import records locations and initializes selected
governance content; it does not clone, copy, or execute source.

## Inspect graph health per repository

Each repository keeps its own graph build identity and diagnostics. The workspace view
coordinates them without masking a stale or unavailable participant. Before product
definition or change analysis, confirm every required owned repository is healthy and
dependency limitations are visible.

A partial query can still be useful, but it must not be presented as a complete product
baseline.

## Keep product inference within ownership

Business and product-definition inference can use bounded evidence from owned
repositories. Dependency documents and implementation belong to another product and do
not silently become requirements or technical direction. The authority records only the
integration surfaces this product must honor.

Similarly, planning routes implementation to owned repositories. A dependency change
becomes cross-product coordination and a separate governed change under its owner.

## Follow a cross-repository feature

An invitation feature may affect an owned web app, API, and infrastructure repository and
consume an identity service owned elsewhere. The workspace dossier can plan customer UI,
API, persistence, configuration, telemetry, and integrated verification across the owned
set. It uses the identity dependency as bounded contract context and records any required
provider change without writing it.

Verification compares each owned repository with its own approved baseline. An unchanged
participant, missing repository, or unexpected dependency edit remains visible.

## Reconcile topology changes explicitly

Adding or reclassifying a repository can change product inference and implementation
routing. Rebuild graphs and reassess current authority. Omitting a repository from a later
import does not remove it, and a participant cannot become a second authority silently.

## Takeaway

Use one authority for one product and reuse the ecosystem identity across related product
workspaces. Federate repository evidence while preserving repository-qualified identity,
independent health, and ownership.

## Canonical CIS sources

- [`cis workspace init`](../manual/cis_workspace_init.md)
- [`cis repo import`](../manual/cis_repo_import.md)
- [Business requirements governance](../specs/business-requirements-governance-spec.md)
- [Technical-intent governance](../specs/technical-intent-governance-spec.md)
- [High-level product-definition wizard](../specs/high-level-product-definition-wizard-spec.md)

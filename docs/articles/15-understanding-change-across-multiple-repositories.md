---
title: "Understanding Change Across Multiple Repositories"
type: article
status: Draft
series: "Repository Knowledge and Context"
series_order: 6
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: on workspace or federation change
summary: "How one product authority combines owned repositories and bounded dependency context without erasing ownership."
cis:
  stable_id: change-impact-studio:article:change-across-multiple-repositories
---

# Understanding change across multiple repositories

A product can be one business system and many Git repositories. Web applications,
services, mobile clients, contracts, infrastructure, and documentation evolve under
different release cycles and ownership.

A repository-local view is necessary but insufficient. A centralized copy of every fact
is easier to query but quickly becomes stale and undermines local ownership.

Change Impact Studio uses a federated workspace model.

## One product authority, explicit participation

A workspace registers exactly one authority repository for one product's business
requirements, technical intent, and change governance. It also records the wider
ecosystem identity. Imported repositories are explicitly either product-owned or
external dependencies with producer, consumer, or bidirectional relationships.

Participants retain:

- their local source and tests;
- repository-scoped specifications and standards;
- contract inventories;
- component and package evidence; and
- local Git history and baselines.

The authority does not copy those repositories. It records their identities, locations,
documentation roots, roles, and graph builds.

## Identity must be repository-qualified

Names collide across repositories. `UserService`, `GET /health`, or `deploy` may exist
in several participants. Workspace graph nodes include repository identity so queries
and task routing do not confuse them.

Cross-repository relationships are explicit or evidence-backed. Importing repositories
does not infer that similarly named components depend on each other.

## Build graphs independently

Each repository graph has its own build identity, diagnostics, and freshness. A
workspace build coordinates them without turning partial failure into a false complete
generation.

If one repository is unavailable or invalid, its status remains independent and visible.
Queries can still use healthy participants while change readiness reports the missing
evidence.

## Keep authority baselines current

The canonical business requirements record owned-repository graph baselines. Technical
intent records business content, owned builds, and dependency surfaces. Semantic changes,
assessed owned-source drift, or owned-repository-set changes can make authority stale.
Dependency freshness remains visible without silently invalidating product authority.

A new graph build for the same unchanged participant may update provenance without
requiring a duplicate decision. The system distinguishes managed baseline movement from
changed meaning.

## Route work to owners

Planning uses product-owned repository roles when available. Frontend work routes to
frontend repositories, persistence to data owners, and infrastructure to deployment
repositories. Dependencies can supply or consume contracts, but a change to their
implementation requires a separate change under their owning product workspace.
Cross-cutting coordination remains in the authority workspace.

The plan retains repository targets so an agent envelope cannot silently implement
workspace-wide scope from one checkout.

## Takeaway

Multi-repository understanding needs shared intent and federated evidence. Give one
authority repository ownership of one product's meaning. Let owned participants and
dependency repositories retain their local facts. Qualify identities, build graphs
independently, and make cross-repository relationships explicit.

Centralize governance where necessary, not every copy of the system.

## Canonical CIS sources

- [Business requirements governance](../specs/business-requirements-governance-spec.md)
- [Technical-intent governance](../specs/technical-intent-governance-spec.md)
- [Context model and local graph](../specs/context-model-and-graph-spec.md)
- [`cis repo import`](../manual/cis_repo_import.md)

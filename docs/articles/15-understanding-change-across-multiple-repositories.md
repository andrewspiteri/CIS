---
title: "Understanding Change Across Multiple Repositories"
type: article
status: Active
series: "Repository Knowledge and Context"
series_order: 6
owner: "Andrew Spiteri"
last_reviewed: "2026-09-10"
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
Dependencies may be further limited to named components. They supply integration context
but are excluded from product-definition inference and implementation routing.

Participants retain:

- their local source and tests;
- repository-scoped specifications and standards;
- contract inventories;
- component and package evidence; and
- local Git history and baselines.

The authority does not copy those repositories. It records their identities, locations,
documentation roots, roles, and graph builds.

The distinction between role and participation matters:

| Repository boundary | May define this product | May receive implementation work here | Typical use |
|---|---|---|---|
| Authority, owned | Yes | Yes, when it contains implementation | Product documents and coordination |
| Participant, owned | Supplies governed product evidence | Yes | Web, API, mobile, data, or infrastructure delivery |
| Participant, dependency | No | No | External producer or consumer context |

A dependency is not “less important.” It may carry the contract most likely to break.
The restriction means only that another product authority owns its requirements,
implementation decisions, and acceptance.

## Identity must be repository-qualified

Names collide across repositories. `UserService`, `GET /health`, or `deploy` may exist
in several participants. Workspace graph nodes include repository identity so queries
and task routing do not confuse them.

Cross-repository relationships are explicit or evidence-backed. Importing repositories
does not infer that similarly named components depend on each other.

## Follow a cross-repository change

Suppose an ordering product changes the event emitted when an order is cancelled. The
authority may identify:

- an owned API repository that publishes the event;
- an owned operations repository that monitors delivery failures;
- a dependency repository for a customer-notification product that consumes the event;
- the versioned event contract and compatibility decision; and
- separate tests and release paths in each owned repository.

The ordering workspace can plan its publisher and operational work. It can use the
notification repository as bounded evidence about consumer impact, but it cannot assign
write-capable agent work there. If the consumer must change, that finding becomes a
coordination obligation and a separately governed change for the notification product.

This prevents a convenient multi-repository checkout from becoming an accidental
organization-wide authority.

## Build graphs independently

Each repository graph has its own build identity, diagnostics, and freshness. A
workspace build coordinates them without turning partial failure into a false complete
generation.

If one repository is unavailable or invalid, its status remains independent and visible.
Queries can still use healthy participants while change readiness reports the missing
evidence.

Partial availability must not be reported as a complete product view. A context search
may return useful results from five healthy repositories while clearly identifying the
sixth as unavailable or stale. Impact approval can then decide whether the missing
repository is irrelevant, requires recovery, or blocks planning.

## Keep authority baselines current

The canonical business requirements record owned-repository graph baselines. Technical
intent records business content, owned builds, and dependency surfaces. Semantic changes,
assessed owned-source drift, or owned-repository-set changes can make authority stale.
Dependency freshness remains visible without silently invalidating product authority.

A new graph build for the same unchanged participant may update provenance without
requiring a duplicate decision. The system distinguishes managed baseline movement from
changed meaning.

Repository-set changes deserve particular care. Adding an owned participant can reveal
requirements or implementation surfaces absent from the prior product baseline. Removing
one is not an incidental registry cleanup; it changes the claimed product boundary and
needs explicit future governance rather than omission from an import command.

## Route work to owners

Planning uses product-owned repository roles when available. Frontend work routes to
frontend repositories, persistence to data owners, and infrastructure to deployment
repositories. Dependencies can supply or consume contracts, but a change to their
implementation requires a separate change under their owning product workspace.
Cross-cutting coordination remains in the authority workspace.

The plan retains repository targets so an agent envelope cannot silently implement
workspace-wide scope from one checkout.

## Avoid the shadow-repository trap

A common response to fragmented knowledge is to copy contracts and documentation into a
central repository. The copy is initially convenient and eventually ambiguous: teams no
longer know whether the local contract or central version controls, and updates arrive at
different times.

Federation keeps one product authority without duplicating participant truth. Stable IDs,
paths, digests, and graph build identities let the authority refer to local facts. When a
cross-product contract needs shared governance, each product can record its own obligation
and link to the same external version or reference rather than pretending one workspace
owns both products.

## Review the topology before the change

Before impact analysis, confirm the product and ecosystem IDs, the single authority,
owned versus dependency participation, dependency direction, component scopes, repository
availability, and graph freshness. Those facts determine where meaning can be inferred,
where implementation may be routed, and which external coordination cannot be completed
inside the workspace.

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

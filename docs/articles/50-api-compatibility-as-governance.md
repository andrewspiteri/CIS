---
title: "API Compatibility as a Governance Problem"
type: article
status: Active
series: "Standards and Engineering Policy"
series_order: 9
owner: "Andrew Spiteri"
last_reviewed: "2026-09-10"
review_cadence: on API-governance change
summary: "Compatibility depends on supported baselines, consumers, lifecycle, ownership, and human policy—not only a schema diff."
cis:
  stable_id: change-impact-studio:article:api-compatibility-governance
---

# API compatibility as a governance problem

An OpenAPI diff can identify structural change. It cannot decide which baselines must be
supported, who owns the break, whether consumers have migrated, or which exception is
acceptable.

## Inventory the governed contract

CIS relates implemented routes, an API dictionary, OpenAPI operations, ownership,
consumers, exposure, authentication, cache behavior, errors, lifecycle, and evidence.

## Compare every supported baseline

Compatibility is forward-transitive across the configured support window. A current
contract should not be considered safe merely because it is compatible with the newest
baseline while breaking an older still-supported consumer.

## Source and generated contracts have different roles

Source discovery proves implemented operations within supported extractors. OpenAPI is a
generated contract baseline. The row-level inventory carries governance fields neither
source nor generated schema can infer safely.

Reference governance preserves the API dictionary's stable identities, lifecycle,
provider provenance, source evidence, and drift. Reconciliation may refresh Draft
observations, but it cannot silently rewrite reviewed contract meaning.

## Dynamic provider surfaces need reviewed declarations

SDK-owned route families may not be statically enumerable. A version-pinned, reviewed
declaration can identify their bounded surface without weakening ordinary endpoint checks.

## Breaking change is a decision

The diff supplies evidence. Compatibility policy, consumer impact, versioning, rollout,
and exception authority determine the outcome.

## Compatibility begins with a promise

A structural diff becomes meaningful only after the product defines what it promises to
whom and for how long. The API profile records supported versions or baselines, exposure,
consumer expectations, and lifecycle. Without that policy, a tool can report change but
cannot classify its product consequence.

Compatibility can include more than schema shape: status codes, problem types, headers,
authentication, ordering, pagination, defaults, nullability, timing, idempotency, cache
semantics, and documented side effects may all matter to consumers.

## Additive is not automatically safe

Adding a response field is usually structurally compatible and can still break strict
deserializers. Adding an enum member may break exhaustive clients. Making a field optional
may change business guarantees. Returning a new error may alter retry behavior.

The deterministic comparator provides necessary evidence. Consumer inventory and governed
policy interpret that evidence within the supported window.

## Compare the entire support window

Suppose versions 1, 2, and 3 remain supported. A current contract compatible with version
3 but not version 1 still breaks the declared promise. CIS compares every configured
baseline and reports which operations and consumers are exposed.

Retiring version 1 is a lifecycle decision with migration and communication evidence; it
is not a convenient way to make the diff green.

## Connect contracts to owners and consumers

The API dictionary gives stable operation and problem identities, owners, exposure,
authentication, cache behavior, lifecycle, and source provenance. Graph and reference
evidence connect operations to implementation, tests, SDKs, documentation, and consumers.

In a multi-product ecosystem, dependency consumers remain under their own authority. The
provider workspace records coordination impact but does not assign their implementation
work silently.

## Govern dynamic surfaces narrowly

An identity SDK or framework may own route families that static discovery cannot enumerate.
A reviewed declaration should pin provider and version, constrain the route or contract
family, state lifecycle and evidence, and receive protocol-specific validation. A broad
wildcard that suppresses unknown endpoints weakens governance for the rest of the API.

## Resolve breaks as product decisions

Options can include preserving the old behavior, adding a transition, introducing a new
version, migrating consumers, or approving a bounded exception. Record alternatives,
evidence, owner, rationale, rollout, telemetry, deprecation, and rollback before dependent
implementation proceeds.

The break may be worth making. Governance exists to make its consequence and authority
explicit, not to prohibit evolution.

## Keep reference drift visible

Source discovery, OpenAPI, inventory rows, external specifications, and deployed evidence
can disagree. Reconciliation updates Draft observations and surfaces conflicts; it does
not silently replace reviewed meaning. Compatibility evidence is trustworthy only when
the compared baselines and governing reference digests are current.

## Use a release-ready compatibility record

The final record should name the changed operations, compared baseline versions,
affected consumers, structural differences, chosen strategy, migrations, deprecation
dates, tests, rollout signals, unavailable evidence, and approving authority. That makes
release communication actionable and prevents a single “breaking: no” flag from carrying
more certainty than the analysis supports.

## Takeaway

Treat API compatibility as governed product behavior. Combine deterministic diffs with
supported-baseline policy, consumer identity, lifecycle, ownership, and human decisions.

## Canonical CIS sources

- [API design and governance](../specs/api-design-and-governance-spec.md)
- [API governance profile](../references/api-governance-profile.md)
- [`cis api diff`](../manual/cis_api_diff.md)
- [`cis api validate`](../manual/cis_api_validate.md)
- [Reference governance and drift](../specs/reference-governance-and-drift-spec.md)

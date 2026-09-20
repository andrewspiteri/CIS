---
title: "Governing a Public Cached Endpoint"
type: article
status: Active
series: "CIS in Practice"
series_order: 6
owner: "Andrew Spiteri"
last_reviewed: "2026-09-10"
review_cadence: on public-cache policy change
summary: "A practical delivery chain for an unauthenticated endpoint that cannot reach persistence directly."
cis:
  stable_id: change-impact-studio:article:govern-public-cached-endpoint
---

# Governing a public cached endpoint

Suppose a public landing page needs `GET /catalog/featured`. The route is simple; the
failure and data-exposure boundaries are not.

## Classify it explicitly

The operation is an unauthenticated public application endpoint, not an identity protocol
route. `PUBLIC-ENDPOINT-CACHE` applies.

## Design the dependency boundary

The controller or handler reads through an application/query abstraction and never
accesses a database or repository directly. Cache population remains behind that boundary.

## Decide cache behavior

The plan defines key variation, freshness, invalidation, stampede control, stale response,
failure fallback, and data exposure. These are product decisions, not library defaults.

## Plan the evidence

Architecture tests check prohibited persistence dependencies. Integration tests exercise
hits, misses, invalidation, and failures. Observability shows hit rate, latency, fallback,
and errors. Security review confirms that cached variants cannot expose private data.
`cis test reconcile` and `cis security reconcile` can normalize the resulting evidence,
but a missing or malformed expected result remains invalid evidence rather than a pass.

## Define the public contract first

For `GET /catalog/featured`, record the operation identity, owner, exposure,
authentication, response and problem shapes, consumers, cache behavior, lifecycle, and
supported baselines. Decide which fields are safe for every anonymous caller and avoid
including user-specific variation accidentally.

The route is public application behavior. It is not an OAuth callback or token exchange,
so `PUBLIC-ENDPOINT-CACHE` applies.

## Make cache variation explicit

Possible key dimensions include locale, market, tenant-visible catalogue, device class,
or a feature version. Every dimension increases cardinality and creates a data-isolation
question. Headers or cookies not included in the key must not change private response
content behind the cache.

Define freshness, maximum staleness, invalidation, warm-up, stampede control, and capacity.
“Cache for five minutes” is not a complete failure contract.

## Keep transport away from persistence

The route or controller calls an application/query abstraction. That layer coordinates
cache lookup, allowed projection, and population. Persistence remains behind the
application boundary on hits, misses, refreshes, and fallback.

Architecture tests should reject a repository or database dependency in the public
transport. Code review checks dynamic or framework paths the evaluator cannot prove.

## Decide degraded behavior

| Condition | Decision to govern |
|---|---|
| Cache miss, source healthy | Populate once and return governed projection |
| Cache unavailable | Fail, use bounded fallback, or read source through the application layer |
| Source unavailable, stale entry exists | Serve within declared stale window or fail |
| Concurrent misses | Coalesce, lock, or tolerate bounded duplicate work |
| Invalidation fails | Alert, expire naturally, or disable affected content |
| Variant uncertainty | Fail closed rather than risk cross-user data exposure |

These choices affect availability, cost, privacy, and customer expectation and require
human product and technical authority.

## Assemble proportionate evidence

Use architecture checks for dependency isolation, unit tests for keys and policy,
integration tests for hit/miss/invalidation/failure, contract comparison for consumers,
security tests for data exposure and poisoning, and telemetry checks for hit rate, latency,
fallback, source errors, and stampedes. Reconciled test and security artifacts must be
parseable and bound to the current suite and revision.

## Verify operational readiness

The runbook should explain invalidation, emergency disablement, cache purge, capacity,
alert thresholds, and diagnosis without requiring direct database access from the public
layer. Acceptance records unavailable failure simulations and residual risk explicitly.

## Review before accepting the endpoint

Trace the API operation to its requirement, cache policy, application abstraction,
persistence boundary, contract tests, security evidence, and telemetry. Confirm that every
variant is public-safe, every failure path remains inside the architecture, and every
required artifact is current and parseable. Record any unavailable load or fault evidence
as residual risk rather than assuming the simple route cannot fail dangerously.

## Takeaway

A public endpoint plan must include the cache and trust boundary, not only the route and
query. Make failure, freshness, persistence isolation, security, and evidence explicit.

## Canonical CIS sources

- [Public endpoint caching policy](../specs/public-endpoint-caching-policy-spec.md)
- [API design and governance](../specs/api-design-and-governance-spec.md)
- [Observability and operations task type](../specs/observability-operations-task-type.md)
- [Security testing and evidence](../specs/security-testing-and-evidence-spec.md)

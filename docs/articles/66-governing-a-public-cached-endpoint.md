---
title: "Governing a Public Cached Endpoint"
type: article
status: Draft
series: "CIS in Practice"
series_order: 6
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
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

## Takeaway

A public endpoint plan must include the cache and trust boundary, not only the route and
query. Make failure, freshness, persistence isolation, security, and evidence explicit.

## Canonical CIS sources

- [Public endpoint caching policy](../specs/public-endpoint-caching-policy-spec.md)
- [API design and governance](../specs/api-design-and-governance-spec.md)
- [Observability and operations task type](../specs/observability-operations-task-type.md)


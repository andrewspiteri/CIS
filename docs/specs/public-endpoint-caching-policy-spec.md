---
title: "Public Endpoint Caching Policy"
type: specification
status: Draft
scope: "Product:ChangeImpactStudio"
owner: "Andrew Spiteri"
last_reviewed: "2026-08-20"
review_cadence: "on public endpoint, caching, or data-access change"
cis:
  stable_id: change-impact-studio:spec:public-endpoint-caching-policy
---

# Public Endpoint Caching Policy

## Rule

Every unauthenticated public application HTTP/API endpoint must use a governed server-side or edge
cache. Its route, controller, endpoint handler, or transport adapter must not access
a database, database client/context, query provider, or repository directly,
including on cache miss.

The endpoint response path always traverses the cache abstraction. Cache population
is delegated behind an application/query abstraction, which owns any required source
read. This keeps transport code independent of persistence and makes cache behavior
testable. A documented exception requires an explicit human-approved architecture
decision; omission, low traffic, or implementation convenience is not an exception.

Identity-establishment protocol routes—such as OAuth callbacks, token exchanges, and
session-creation commands—are not public application endpoints merely because they run
before authentication. They must be explicitly classified, use `no-store`, preserve the
same transport-to-application dependency boundary, and receive protocol-specific security
review. This distinction cannot be used to reclassify an ordinary public read endpoint.

This rule does not declare authenticated/customer/backoffice responses publicly
cacheable. Their caching and data-access policy remains governed by their security,
privacy, and consistency requirements.

## Required endpoint contract

Each unauthenticated operation records:

- endpoint identity, method, route, public/anonymous access, and response shape;
- cache layer and owner;
- cache key and every representation dimension, including locale, tenant, market,
  feature/configuration version, or other applicable `Vary` input;
- TTL, freshness/staleness window, invalidation trigger, and refresh owner;
- HTTP `Cache-Control`, `ETag`, `Vary`, CDN/edge, or equivalent semantics;
- stampede/single-flight protection and bounded cache-miss behavior;
- source dependency failure, stale-serving, timeout, and unavailable behavior;
- payload sensitivity and proof that personalized, restricted, secret, or unsafe
  data cannot enter the public cache; and
- the application/query abstraction that populates the cache without exposing a
  database/repository dependency to the endpoint boundary.

The API dictionary records `Cache policy` and `Data access path` for each operation.

## Architecture boundary

The public route/controller/handler may depend on a cache-backed query abstraction.
It may not depend on or construct an ORM context, database connection/client,
repository, persistence query provider, or equivalent storage adapter. On a cache
miss it asks the abstraction to populate or refresh the cache; it does not perform a
fallback database query itself.

Architecture tests should enforce the dependency direction where the repository's
language and framework permit it. Runtime/integration tests remain required because
dependency checks alone cannot prove that every response traverses the cache.

## Security and operational behavior

- Cache keys include all inputs that materially change the representation.
- Public payloads contain no personalized, tenant-restricted, secret, or sensitive
  values unless an explicitly approved policy proves safe isolation.
- Keys, tags, logs, metrics, and traces do not leak sensitive input or payload data.
- Refresh and invalidation are bounded, idempotent, and protected against stampedes.
- Hit, miss, refresh, stale, eviction, latency, and failure behavior are observable.
- Alerts and a runbook cover dependency failure, refresh failure, low hit ratio,
  excessive churn, stale data, and cache outage/bypass risks.

## Required evidence

- cache hit, miss, refresh, invalidation, expiry, stale, and failure tests;
- concurrency/stampede and bounded-load tests;
- response-header and cache key/`Vary` tests;
- cache-poisoning, representation-isolation, enumeration, and payload-safety tests;
- architecture/dependency tests rejecting direct database/repository access from the
  unauthenticated endpoint boundary;
- integration evidence proving the endpoint response traverses the cache abstraction;
  and
- redacted operational telemetry and runbook validation.

Planning marks these obligations with `PUBLIC-ENDPOINT-CACHE` in Security, API
contract, Backend, Observability, Verification, and Independent assurance tasks.

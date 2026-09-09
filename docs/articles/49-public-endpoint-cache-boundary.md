---
title: "The Public Endpoint Cache Boundary as a Worked Example"
type: article
status: Draft
series: "Standards and Engineering Policy"
series_order: 8
owner: "Andrew Spiteri"
last_reviewed: "2026-09-09"
review_cadence: on public-endpoint policy change
summary: "How one policy marker expands into cache, persistence, security, observability, and verification obligations."
cis:
  stable_id: change-impact-studio:article:public-endpoint-cache-boundary
---

# The public endpoint cache boundary as a worked example

A rule becomes useful when it changes the plan and evidence, not merely when it appears
in a standards document.

CIS uses `PUBLIC-ENDPOINT-CACHE` for unauthenticated public application endpoints.

## The boundary rule

Every such endpoint is cached, and its public route, controller, or handler never
accesses a database or repository directly—even on cache miss. Population belongs behind
an application or query abstraction.

## Planning expands the obligation

An affected feature receives explicit work for:

- cache key and variation;
- freshness and invalidation;
- stampede behavior;
- failure and stale-response policy;
- authorization and data exposure;
- persistence isolation;
- observability; and
- independent verification.

## Identity protocol routes differ

OAuth callbacks, token exchanges, and session creation may run before authentication
without becoming ordinary public application endpoints. They remain `no-store` and
receive protocol-specific security review while preserving the transport-to-application
persistence boundary.

## Enforcement spans several methods

Plans and task documents can be validated deterministically. Architecture tests can
enforce prohibited dependencies. Browser and integration tests can exercise cache
behavior. Human review owns failure and risk decisions.

## Start from classification, not middleware

The first decision is whether the operation is an unauthenticated public application
endpoint or a pre-authentication identity protocol route. Both may be callable without a
session, but their security and cache behavior differ. Applying a blanket “anonymous means
cacheable” rule to token exchange or callback routes would be dangerous.

The governed API inventory should record exposure, authentication class, cache policy,
ownership, consumers, errors, and lifecycle so the distinction survives framework
implementation details.

## Design the cache as product behavior

For `GET /catalog/featured`, planning should answer:

- which request dimensions vary the key;
- what data may enter a public response;
- freshness and maximum stale age;
- invalidation source and failure behavior;
- stampede prevention and concurrency;
- fallback when cache or source is unavailable;
- response headers and consumer expectations; and
- telemetry for hit, miss, latency, fallback, and error.

Library defaults cannot resolve these questions because they express product risk and
operational intent.

## Preserve the persistence boundary on misses

The public controller or route calls an application/query abstraction. That abstraction
may coordinate cache lookup and population and may eventually reach persistence. The
transport does not inject or call a repository directly when the cache misses.

This shape keeps data access, authorization, filtering, and failure behavior governed in
one application boundary. A conditional direct database call is still a violation even
if the happy path uses cache.

## Map the rule to several evidence types

| Obligation | Useful evidence |
|---|---|
| Route classification | Governed API inventory and review |
| No direct persistence dependency | Architecture or compiler-backed test |
| Key and variation behavior | Focused unit and integration tests |
| Hit, miss, invalidation, stale and failure behavior | Integration and fault tests |
| Public data exposure | Security review and negative tests |
| Consumer behavior | Contract and browser evidence |
| Operational visibility | Telemetry and runbook review |

No single evaluator proves the whole policy. The stable rule links these obligations into
one assurance chain.

## Treat exceptions as high visibility

A legacy endpoint that cannot meet the boundary needs an exact rule exception,
compensating controls, owner, scope, expiry or review condition, and residual-risk
approval. Disabling an architecture test or marking the route internal without evidence
would only hide the deviation.

## Verify the policy through change

An initially compliant endpoint can drift when a new field, fallback, provider, or error
path is added. Impact and planning should reactivate the cache and persistence obligations
for every material change. Policy is not proven forever by the test that accompanied the
first implementation.

## Review the whole chain

Before acceptance, trace the stable policy rule to the API inventory, accepted impact,
application abstraction, implementation, cache configuration, architecture test,
integration and failure tests, telemetry, exceptions, and human risk decision. A missing
link does not automatically prove violation, but it identifies the exact evidence or
authority that still needs review.

## Takeaway

A governed policy should propagate into task selection, architecture, tests, operations,
and acceptance. The cache boundary demonstrates how one stable rule becomes a complete
engineering obligation.

## Canonical CIS sources

- [Public endpoint caching policy](../specs/public-endpoint-caching-policy-spec.md)
- [Change impact and bounded planning](../specs/change-impact-and-planning-spec.md)
- [API design and governance](../specs/api-design-and-governance-spec.md)

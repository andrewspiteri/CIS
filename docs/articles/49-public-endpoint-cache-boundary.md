---
title: "The Public Endpoint Cache Boundary as a Worked Example"
type: article
status: Draft
series: "Standards and Engineering Policy"
series_order: 8
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
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

## Takeaway

A governed policy should propagate into task selection, architecture, tests, operations,
and acceptance. The cache boundary demonstrates how one stable rule becomes a complete
engineering obligation.

## Canonical CIS sources

- [Public endpoint caching policy](../specs/public-endpoint-caching-policy-spec.md)
- [Change impact and bounded planning](../specs/change-impact-and-planning-spec.md)
- [API design and governance](../specs/api-design-and-governance-spec.md)


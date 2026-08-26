---
title: "Plan Public, Customer, and Backoffice Experiences Separately"
type: article
status: Draft
series: "Change Impact and Planning"
series_order: 8
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: on frontend-classification change
summary: "Why each affected user-facing classification needs its own behavior, design, implementation, and evidence chain."
cis:
  stable_id: change-impact-studio:article:plan-frontend-types-separately
---

# Plan public, customer, and backoffice experiences separately

One logical feature can appear in several user contexts. A public visitor, authenticated
customer, and internal operator may perform related actions with different information,
permissions, risks, and success evidence.

Putting all three into one “frontend task” loses those distinctions.

## Three closed classifications

CIS uses:

- **public** for anonymous acquisition, information, registration, and other visitor-facing experiences;
- **customer** for authenticated end-customer product behavior; and
- **backoffice** for internal administration, support, and operations.

Non-frontend requirements use `not-applicable`.

## One requirement row per affected type

Comma-separated classifications are invalid. If one behavior affects multiple types,
the feature uses a stable requirement row for each. This preserves precise acceptance,
task assignment, and evidence ownership.

## Each type gets a matched chain

Frontend planning creates a separate sequence for each affected classification:

```text
Requirement
  → textual wireframe
  → visual and interaction design
  → implementation
  → shared and type-specific verification
```

The customer flow cannot be treated as evidence that the backoffice flow was designed
or verified.

## Classification carries security meaning

Public application endpoints may activate mandatory cache and persistence-isolation
obligations. Pre-authentication identity protocol routes remain a separate `no-store`
class. Customer and backoffice surfaces have different authentication, authorization,
and data-exposure expectations.

Classification is therefore an engineering control, not a visual label.

## Takeaway

When a feature crosses public, customer, and backoffice experiences, preserve one
requirement and matched delivery chain per type. Shared implementation can still be
reused, but behavior, authority, and evidence remain explicit.

## Canonical CIS sources

- [Change impact and bounded planning](../specs/change-impact-and-planning-spec.md)
- [Wireframe task type](../specs/wireframe-task-type.md)
- [Frontend implementation task type](../specs/frontend-implementation-task-type.md)
- [Public endpoint caching policy](../specs/public-endpoint-caching-policy-spec.md)

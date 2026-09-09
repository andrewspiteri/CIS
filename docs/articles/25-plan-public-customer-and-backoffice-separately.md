---
title: "Plan Public, Customer, and Backoffice Experiences Separately"
type: article
status: Draft
series: "Change Impact and Planning"
series_order: 8
owner: "Andrew Spiteri"
last_reviewed: "2026-09-09"
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

## Similar nouns do not imply the same experience

An “invitation” can appear on a public redemption page, in a customer's list settings,
and in a support console. The domain object is shared, but the actors and authority are
not:

| Experience | Primary concern | Typical risk |
|---|---|---|
| Public | Explain and redeem a bounded invitation before authentication | Enumeration, caching, privacy, abuse |
| Customer | Issue, view, and revoke invitations for owned lists | Authorization, state clarity, accidental loss |
| Backoffice | Diagnose failures and perform authorized support actions | Privilege, auditability, sensitive data exposure |

One generic acceptance criterion such as “the user can manage invitations” cannot prove
all three.

## Separate requirements before tasks

The distinction begins in the feature specification. Each requirement names one
classification, actor, observable behavior, states, and acceptance criteria. Planning can
then create a matched wireframe, design, implementation, and evidence chain.

If a single row lists `public,customer`, ownership becomes ambiguous: which navigation,
authentication state, error behavior, and reviewer apply? Splitting the rows preserves a
shared feature while making each obligation testable.

## Reuse implementation without merging authority

Separate planning does not forbid shared components, design tokens, client libraries, or
backend services. It prevents reuse from erasing experience-specific behavior. A shared
invitation card may render in public and customer surfaces, while each wrapper controls
actions, data, and navigation appropriate to its actor.

The plan should identify common implementation deliberately and retain separate
acceptance. Passing the component's unit tests does not prove that either end-to-end flow
is correct.

## Design complete state models

Each classification needs more than a happy screen. Plan loading, empty, invalid,
expired, revoked, unauthorized, unavailable, success, and recovery states where relevant.
Accessibility, responsive behavior, keyboard flow, focus management, and understandable
errors belong to the experience evidence, not to a final cosmetic review.

Backoffice states often need additional audit and confirmation. Public states need to
avoid disclosing whether private resources exist. Customer states need clear ownership
and consequences before destructive actions.

## Carry the distinction into APIs and security

Frontend classification can activate backend obligations. A public application endpoint
inherits cache and persistence-isolation policy; an OAuth callback is instead an identity
protocol route and remains `no-store`. Customer and backoffice endpoints may share
authentication infrastructure while requiring different permissions and data shapes.

The UI label does not establish these rules by itself. It routes planning to the relevant
API, permission, privacy, and evidence contracts.

## Verify both shared and specific behavior

Shared tests can cover reusable component logic and design-system rules. Type-specific
browser or integration tests cover actor permissions, navigation, content, failures, and
data exposure. Final verification should show that every classified requirement has its
own evidence and that shared implementation did not create an unauthorized cross-surface
shortcut.

## Takeaway

When a feature crosses public, customer, and backoffice experiences, preserve one
requirement and matched delivery chain per type. Shared implementation can still be
reused, but behavior, authority, and evidence remain explicit.

## Canonical CIS sources

- [Change impact and bounded planning](../specs/change-impact-and-planning-spec.md)
- [Wireframe task type](../specs/wireframe-task-type.md)
- [Frontend implementation task type](../specs/frontend-implementation-task-type.md)
- [Public endpoint caching policy](../specs/public-endpoint-caching-policy-spec.md)

---
title: "Planning a Cross-Surface Authentication Change"
type: article
status: Draft
series: "CIS in Practice"
series_order: 5
owner: "Andrew Spiteri"
last_reviewed: "2026-09-09"
review_cadence: on authentication or planning-policy change
summary: "A worked planning example across public discovery, identity protocol, customer UI, API, persistence, security, and operations."
cis:
  stable_id: change-impact-studio:article:cross-surface-authentication-change
---

# Planning a cross-surface authentication change

“Add social sign-in” appears to be an authentication integration. The complete change
can affect several differently governed surfaces.

## Separate the routes

A public application endpoint may tell the client which sign-in modes are available and
therefore receive cache and persistence-isolation obligations. OAuth callbacks, token
exchanges, and session creation are identity-protocol routes and remain `no-store`.

## Separate user experiences

The public sign-in entry, authenticated customer account state, and backoffice support
view receive distinct requirement and evidence chains.

## Include contract and provider boundaries

Planning covers redirect and error contracts, SDK-owned dynamic route declarations,
provider configuration, secrets, abuse controls, account linking, compatibility, and
failure behavior.

## Include lifecycle and operations

Tasks address session creation, revocation, telemetry, provider outage behavior, rollout,
and verification—not only the successful callback.

## Resolve decisions first

Account-linking policy, supported providers, identity ownership, and fallback behavior
are human decisions required before implementation.

Those decisions should be resolved against the active product-definition baseline and
the governed configuration, permission, API, and reference inventories. Discovery can
surface likely implications, but it must not silently invent a security or identity
policy.

## Start with actors and trust boundaries

Social sign-in involves at least the visitor, existing customer, identity provider,
application, support operator, and attacker. Data crosses the browser, provider redirect,
callback route, token exchange, account-linking service, and session boundary. Map those
actors and flows before selecting SDK methods.

The map exposes which behavior is public application discovery, which is identity
protocol, which requires an authenticated customer, and which belongs only to privileged
support.

## Record material decisions

The plan should not delegate these choices to implementation:

- supported providers and environments;
- whether sign-in can create an account;
- account-linking proof and conflict behavior;
- email or subject-identifier trust;
- session lifetime and revocation;
- provider outage and fallback;
- consent, privacy, and audit requirements; and
- compatibility and rollout strategy.

Each decision names options, evidence, authority, rationale, and the task gate it blocks.

## Build separate contract chains

The public discovery response may be cached and must not expose private configuration.
Authorization redirects and callbacks are protocol routes with `no-store`, strict state
and nonce handling, bounded errors, and no direct transport-to-persistence shortcut.
Customer account settings require authenticated permission and clear link/unlink states.
Backoffice support needs narrower data, stronger audit, and explicit privilege.

Shared nouns do not justify one generic endpoint or screen requirement.

## Sequence the work

A safe plan can approve identity and account-linking decisions, define API and error
contracts, design public and customer states, implement backend protocol behavior,
configure provider credentials and environments, implement UI flows, add telemetry and
runbooks, then perform integrated security and compatibility verification.

Secret values remain provider-native or environmental. Repository configuration records
names and expectations, never credentials.

## Test adversarial and failure behavior

Evidence should cover invalid or replayed state, expired authorization, provider denial,
changed email, account collision, callback tampering, rate abuse, provider outage, partial
linking, session revocation, audit failure, and safe user recovery. Browser tests confirm
navigation and messaging; integration and security tests confirm protocol and data
boundaries.

A successful provider callback alone is weak evidence.

## Plan rollout and rollback

Use configuration and telemetry to introduce providers deliberately. Define behavior for
mixed client versions, provider disablement, incident response, and unlink or rollback.
Monitor conversion, callback failures, link conflicts, abuse signals, and session errors
without logging tokens or sensitive identity data.

## Takeaway

Authentication is a cross-surface product change. Classify public and protocol routes
correctly, separate user experiences, and plan security, contracts, lifecycle, and
operations alongside integration code.

## Canonical CIS sources

- [API design and governance](../specs/api-design-and-governance-spec.md)
- [Public endpoint caching policy](../specs/public-endpoint-caching-policy-spec.md)
- [Security and permissions task type](../specs/security-permissions-task-type.md)
- [Change impact and bounded planning](../specs/change-impact-and-planning-spec.md)
- [Product and ecosystem boundary](../specs/product-and-ecosystem-boundary-spec.md)
- [High-level product-definition wizard](../specs/high-level-product-definition-wizard-spec.md)
- [Reference governance and drift](../specs/reference-governance-and-drift-spec.md)

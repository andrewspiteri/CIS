---
title: "Planning a Cross-Surface Authentication Change"
type: article
status: Draft
series: "CIS in Practice"
series_order: 5
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
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

## Takeaway

Authentication is a cross-surface product change. Classify public and protocol routes
correctly, separate user experiences, and plan security, contracts, lifecycle, and
operations alongside integration code.

## Canonical CIS sources

- [API design and governance](../specs/api-design-and-governance-spec.md)
- [Public endpoint caching policy](../specs/public-endpoint-caching-policy-spec.md)
- [Security and permissions task type](../specs/security-permissions-task-type.md)
- [Change impact and bounded planning](../specs/change-impact-and-planning-spec.md)

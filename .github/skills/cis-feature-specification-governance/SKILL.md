---
name: cis-feature-specification-governance
description: Author, validate, review, approve, and refresh classification-aware feature specifications before detailed CIS planning.
---

# CIS feature specification governance

1. Read the active BRD, technical intent, approved high-level backlog item, repository profiles, standards, references, and applicable public/customer/backoffice frontend classification.
2. Start or locate the feature with `cis brd feature status <high-level-item>` and create the governed draft through `cis brd feature validate` workflow; do not invent a second source of truth.
3. Specify actors, outcomes, business rules, state transitions, contracts, authorization and non-disclosure, caching, persistence, migration/recovery, accessibility, observability, rollout, exclusions, and measurable acceptance criteria proportionate to the feature.
4. Route frontend scope as public, customer, or backoffice. Describe required user journeys and review points without selecting unapproved visual implementation details.
5. Define layered test obligations: unit, component, integration, business, architecture, frontend component, browser, security, operational, coverage, and mutation where applicable. Credential-dependent provider smoke may be explicitly unavailable; it is never silently passed.
6. Run `cis brd feature validate <high-level-item>`. Resolve structural, currency, traceability, and cross-document findings before requesting review.
7. Only after explicit human authorization run `cis brd feature approve <high-level-item> --reviewer <human> --reason <rationale>`.
8. A material BRD or technical-intent change makes the feature stale and requires reconciliation. Mechanical validation with no semantic change adds no approval gate.

Preserve stable identities, approval evidence, exclusions, and history. Never approve for the user, silently broaden MVP scope, treat a generated draft as current, or report an unexecuted test obligation as passed.
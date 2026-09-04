---
name: cis-feature-specification-governance
description: Author, validate, review, approve, and refresh classification-aware feature specifications before detailed CIS planning.
---

# CIS feature specification governance

1. Run `cis definition status`. When a wizard session exists, require its final consolidated activation, then read the digest-bound BRD, technical questionnaire and intent, solution design, architecture diagrams, component sheet, dictionary index and dictionaries, UI questionnaire and direction, visual preview, approved high-level backlog item, repository profiles, standards, references, and applicable public/customer/backoffice frontend classification.
2. Start or locate the feature with `cis brd backlog start --item <high-level-item>` and `cis brd feature status --item <high-level-item>`; do not invent a second source of truth.
3. Replace a generated scaffold through bounded agent authoring with `cis agent author feature --item <high-level-item> --provider <provider> --actor <human>`, or complete it manually. Agent authoring may edit only the feature file and cannot approve it.
4. Specify actors, outcomes, business rules, state transitions, contracts, authorization and non-disclosure, caching, persistence, migration/recovery, accessibility, observability, rollout, exclusions, and measurable acceptance criteria proportionate to the feature. Use only the controlled surfaces `frontend`, `backend`, `full-stack`, `mobile`, `native`, `api`, `contract`, `data`, `security`, `delivery`, or `documentation`, and frontend types `public`, `customer`, `backoffice`, or `not-applicable`; put descriptive boundary names in requirement text.
5. Route frontend scope as public, customer, or backoffice. Describe required user journeys and review points without selecting unapproved visual implementation details.
6. Define layered test obligations: unit, component, integration, business, architecture, frontend component, browser, security, operational, coverage, and mutation where applicable. Credential-dependent provider smoke may be explicitly unavailable; it is never silently passed.
7. Run `cis brd feature validate --item <high-level-item>`. Resolve structural, currency, traceability, and cross-document findings before requesting review.
8. Only after explicit human authorization run `cis brd feature approve --item <high-level-item> --reviewer <human> --reason <rationale>`.
9. A material change anywhere in the consolidated product-definition baseline makes the feature binding stale and requires reconciliation. Mechanical validation with no semantic change adds no approval gate.

Preserve stable identities, approval evidence, exclusions, and history. Never approve for the user, silently broaden MVP scope, treat a generated draft as current, or report an unexecuted test obligation as passed.

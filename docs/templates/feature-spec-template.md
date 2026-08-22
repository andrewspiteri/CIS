---
title: "Feature Specification Template"
type: template
status: Active
scope: Repository
owner: Repository maintainer
review_cadence: on template change
targets:
  - TODO: documentation, backend, frontend, mobile, native, data, integration, or delivery
stack:
  - TODO: relevant languages and frameworks
references:
  - TODO: governed BRD, architecture, contract, and reference paths
cis:
  stable_id: change-impact-studio:template:feature-spec
---

# <Feature name>

Copy this template into `specs/` and replace every placeholder, including the title, owner, and stable ID. Change front matter `type` to `feature-specification` and `status` to `Draft` so BRD reconciliation can discover it.

## Feature summary

TODO

## Goals

1. TODO

## Non-goals and explicit exclusions

1. TODO

## Actors and scenarios

| Actor | Scenario | Expected outcome |
|---|---|---|
| TODO | TODO | TODO |

## Functional requirements

Use this table when requirements already have stable IDs. For narrative specifications,
the numbered Goals section is also accepted and receives derived `GOAL-NNN` IDs.

For every UI-bearing requirement, set `Frontend type` to exactly `public`, `customer`,
or `backoffice`. Use `not-applicable` for non-frontend requirements. If one logical
requirement affects multiple frontend types, use one row with a stable ID per type so
scope, acceptance criteria, design, and implementation remain independently traceable.

| ID | Surface | Frontend type | Requirement | Acceptance criteria |
|---|---|---|---|---|
| FEAT-TODO-001 | TODO: frontend, backend, full-stack, mobile, native, API, contract, data, security, delivery, or documentation | TODO: public, customer, backoffice, or not-applicable | TODO | TODO |

## Workflows, states, and invariants

- TODO

## Domain model, data, audit, and migrations

- TODO

## API, contracts, permissions, and visibility

- TODO
- For every unauthenticated/public endpoint: TODO cache key/Vary dimensions, TTL and
  freshness, invalidation/refresh owner, HTTP cache semantics, stampede and failure
  behavior, cache-population abstraction, and proof the endpoint boundary does not
  directly access a database or repository; or Not applicable.

## UX, screens, and accessibility

- TODO: routes, primary and alternate flows, empty/loading/error/denied states, responsive behavior, and accessibility

## Cross-module integrations, commands, and events

- TODO

## Search, projection, and retrieval boundaries

- TODO or Not applicable

## Lifecycle, conversion, and carry-forward

- TODO or Not applicable

## Operational and security considerations

- TODO

## Testing and regression requirements

| Layer | Required evidence |
|---|---|
| Documentation, policy, and contract drift | TODO |
| Domain and application unit tests | TODO or Not applicable |
| Architecture and structural tests | TODO or Not applicable |
| API, service, persistence, and migration integration tests | TODO or Not applicable; name any Testcontainers-provisioned dependency |
| Business acceptance tests | TODO or Not applicable |
| Frontend component and accessibility tests | TODO or Not applicable |
| Browser or platform journeys using page objects or screen models | TODO or Not applicable |
| Mutation testing and test-quality assurance | TODO or Not applicable |
| Coverage, independent assurance, and unrun-check reporting | TODO |

## Traceability and related decisions

- Product requirement:
- Technical intent:
- ADRs:
- References to update:

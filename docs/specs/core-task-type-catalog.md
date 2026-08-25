---
title: "CIS Core Task-Type Catalog"
type: specification
status: Active
version: "1.0"
scope: "Product:ChangeImpactStudio"
owner: "Andrew Spiteri"
last_reviewed: "2026-08-13"
review_cadence: "on task registry change"
cis:
  stable_id: change-impact-studio:spec:core-task-type-catalog
---

# CIS core task-type catalog

## 1. Contract

These definitions implement the common lifecycle, evidence, dependency, complexity,
approval, and extension rules in `task-type-contract-spec.md`. They are registered as
pure data through `ICisTaskTypeProvider`; task instances record the definition key and
version. Core keys are unique. A second provider claiming a core key is a startup error,
not an implicit replacement.

Conditional types are created only from evidence in the imported feature specification.
The always-created types are Coordination, Documentation, Verification, Independent
assurance, and Final delivery. Search/projection is deliberately extension-owned.

Wireframe, Visual design, and Frontend implementation are qualified by frontend type:
`public`, `customer`, or `backoffice`. Planning instantiates one matched chain per
affected type. All other core types remain shared unless their own definition provides
an explicit decomposition dimension.

## 2. Ordered definitions

| Order | Key | Category | Creation evidence | Required predecessor(s) | Primary output | Acceptance and validation focus |
| --- | --- | --- | --- | --- | --- | --- |
| 000 | `core.coordination.scope-guard` | Coordination | Always | None | Coverage/gate ledger and child dispositions | Every requirement, impact, exclusion, task type, dependency, and final disposition is visible; graph and planned-versus-actual validation pass. |
| 010 | `core.design.wireframe` | Wireframe | Screen, route, web/native UI, tab, view, or frontend scope | Coordination | Approved `wireframes.md` | Stable screens, states, actions, side effects, paths, denied behavior, coverage, and exact human-approved digest. |
| 020 | `core.design.visual` | Visual design | Same UI evidence as Wireframe | Wireframe | Self-contained renderer, PNG manifest, `design.md` decision | Guideline provenance, shared shell and complete reusable control/component system, Sharp/SVG runtime, artifacts, hashes, and explicit global-gate decision. |
| 030 | `core.documentation.contracts` | Documentation | Always | Design when UI-bearing | Updated canonical specifications, contracts, decisions, dictionaries, and references | Canonical sources agree with approved behavior and exclusions; strict documentation and drift checks pass. |
| 040 | `core.security.permissions` | Security and permissions | Authorization, authentication, visibility, exposure, sensitive data, secrets, or trust boundaries | Documentation; Design when UI-bearing | Policy/permission/exposure changes and negative tests | Positive and prohibited access paths, redaction, secrets, abuse cases, and security regressions are proven. |
| 050 | `core.data.persistence` | Data and persistence | Model, entity, record, storage, audit, repository, or database behavior | Documentation, Security; Design when UI-bearing | Model, constraints, persistence, audit/retention behavior | Persistence invariants match the approved model without excluded storage; model/repository/constraint tests pass. |
| 060 | `core.data.database-migration` | Database migration | Schema, table, column, index, constraint, migration, or database object | Data; Design when UI-bearing | Repeatable forward migration and rollback/roll-forward position | Idempotency, compatibility, schema result, operational recovery, and rollback status are tested. |
| 070 | `core.data.backfill` | Data migration/backfill | Existing-record transformation, backfill, reconciliation, or reindexing | Database migration; Design when UI-bearing | Restartable transformation and reconciliation report | Dry-run, batching, idempotency, reconciliation, failure recovery, and production-volume behavior pass. |
| 080 | `core.api.contract` | API/consumed contract | Endpoint, request, response, DTO, OpenAPI, ETag, message, or contract | Documentation, Security, Data; Design when UI-bearing | Versioned request/response/message contract | Validation, error semantics, compatibility, schema, consumer integration, and contract drift pass. |
| 090 | `core.backend.behavior` | Backend | Domain, service, handler, command, workflow, rule, or state transition | API, Data, Security where applicable; Design when UI-bearing | Domain/application behavior | Positive, negative, transition, concurrency, integration, and regression checks pass. |
| 100 | `core.frontend.implementation` | Frontend | Web/native UI evidence | Visual design, API, Security where applicable | Production implementation of approved screens | Screen pack and behavior match approval across states/platforms; component, interaction, accessibility, lint, type, build, and browser checks pass. |
| 110 | `core.integration.handoff` | Integration | Cross-module handoff, event, publish/subscribe, linked task, or external system | Backend, Frontend, API where applicable; Design when UI-bearing | Authoritative cross-boundary workflow | Provenance, authorization, state, idempotency, timeout, failure, and recovery behavior pass without a parallel subsystem. |
| 120 | `core.infrastructure.deployment` | Infrastructure/deployment | Terraform, cloud resource, queue/topic, pipeline, configuration, secret, deployment, or flag | Integration where applicable; Design when UI-bearing | Reproducible resources/configuration/pipeline and rollback position | Format, validation, policy, plan, privilege, configuration, deployment smoke, and rollback checks pass. |
| 130 | `core.operations.observability` | Observability/operations | Logging, metrics, traces, alerts, dashboard, runbook, support, or operations | Backend and Infrastructure where applicable; Design when UI-bearing | Telemetry, alerts, dashboards, and support procedure | Emission, redaction, detection, diagnosis, alert behavior, runbook, and failure drill are validated. |
| 140 | `core.lifecycle.carry-forward` | Lifecycle | Conversion, carry-forward, inheritance, archive/restore, history, retention, or lifecycle | Backend and Data where applicable; Design when UI-bearing | Identity/provenance-preserving lifecycle behavior | Transitions avoid duplication and preserve ownership, visibility, history, audit, permission, and retention behavior. |
| 150 | `core.release.rollout` | Rollout/release | Release, staged rollout, canary, compatibility window, rollback, feature flag, or post-release evidence | Infrastructure and Observability where applicable; Design when UI-bearing | Activation/rollback plan, criteria, ownership, communication | Flag behavior, staged activation, compatibility, rollback rehearsal, release notes, and post-release validation pass. |
| 160 | `core.verification` | Verification | Always | Every applicable implementation/operations type; Design when UI-bearing | `test-cases.md`, `test-cases.csv`, live `TC-*` automated-test references, and `verification.md` acceptance/negative-criteria evidence | Every requirement has a stable manual case reflected by recognized automated coverage, and every required/prohibited behavior has reproducible passing evidence or approved residual risk. |
| 170 | `core.assurance.independent` | Independent assurance | Always, depth proportional to risk | Verification; Design when UI-bearing | Independent findings, disposition, and residual risk | Applicable mutation, security, architecture, accessibility, or independent-agent challenge is complete and findings are resolved or accepted. |
| 180 | `core.delivery.final-sweep` | Final delivery | Always | Every applicable delivery task, including Independent assurance; Design when UI-bearing | Planned-versus-actual reconciliation and handoff evidence | All child tasks have valid dispositions; strict docs, affected checks, evidence audit, scope reconciliation, and completion gate pass. |

## 3. Global design barrier

When UI scope exists, every task after Visual design has a direct dependency on it in
addition to its natural predecessors. Successful rendering sets `PausedForReview`.
Only wireframe/design review work may continue until a human approves the exact renderer
and PNG manifest hashes. Rejection preserves hashes and rationale, removes rejected PNGs,
and retains the pause.

The reusable visual catalog is not table-specific. It governs application shells;
buttons and links; text inputs, text areas, selects/dropdowns, checkboxes, radio groups,
and toggles; tabs, breadcrumbs, pagination, and menus; dialogs, alerts, accordions,
avatars, badges, cards, forms, empty states, timelines, filters, and tables. Feature
renderers compose these definitions and do not independently redraw equivalent controls.

## 4. Public endpoint architecture barrier

Every unauthenticated endpoint activates Security, API contract, Backend,
Observability, Verification, and Independent assurance obligations marked
`PUBLIC-ENDPOINT-CACHE`. The endpoint response path must traverse a governed cache,
and its route/controller/handler must not directly depend on or query a database,
database context/client, repository, or persistence query provider, including on
cache miss. Cache population belongs behind an application/query abstraction. The
canonical details are in
[Public Endpoint Caching Policy](public-endpoint-caching-policy-spec.md).

## 4. Evidence and completion

Tasks cannot transition to Complete while acceptance or validation checkboxes remain
open or completion evidence remains a placeholder. Completion snapshots the sanitized
local CIS tool-usage ledger into the task and `verification.md`, including invocation
count, failures, possible token savings, and a digest. The detailed ledger remains local
under `.cis/local/feedback/`.

Deferral must identify the unmet criterion, reason, owner, follow-up, risk, and required
human authority. A blocker, deferral, or skipped check is never converted into a pass.

## 5. Detailed definitions

Coordination, Wireframe, and Visual Design are defined respectively in
`coordination-scope-guard-task-type.md`, `wireframe-task-type.md`, and
`visual-design-task-type.md`. The remaining canonical definitions are:

- [Documentation and contracts](documentation-contracts-task-type.md)
- [Security and permissions](security-permissions-task-type.md)
- [Data and persistence](data-persistence-task-type.md)
- [Database migration](database-migration-task-type.md)
- [Data migration/backfill](data-backfill-task-type.md)
- [API and consumed contract](api-contract-task-type.md)
- [Backend behavior](backend-behavior-task-type.md)
- [Frontend implementation](frontend-implementation-task-type.md)
- [Integration and handoff](integration-handoff-task-type.md)
- [Infrastructure and deployment](infrastructure-deployment-task-type.md)
- [Observability and operations](observability-operations-task-type.md)
- [Lifecycle and carry-forward](lifecycle-carry-forward-task-type.md)
- [Rollout and release](rollout-release-task-type.md)
- [Verification](verification-task-type.md)
- [Independent assurance](independent-assurance-task-type.md)
- [Final delivery sweep](final-delivery-sweep-task-type.md)

The provider conflict/replacement contract is defined by
[Task-Type Extension Provider Policy](task-type-extension-policy.md).

---
title: "Task Type: Infrastructure and Deployment"
type: task-type-definition
status: Draft
version: "0.1"
scope: "Product:ChangeImpactStudio"
owner: "Andrew Spiteri"
last_reviewed: "2026-08-13"
review_cadence: "on planning-model change"
cis:
  stable_id: change-impact-studio:task-type:core.infrastructure.deployment
---

# Task Type: Infrastructure and Deployment

## Identity and boundary

| Property | Value |
| --- | --- |
| Stable type key | `core.infrastructure.deployment` |
| Provider | CIS Plan core module |
| Creation policy | Conditional |
| Required predecessor(s) | Integration where applicable; approved Visual Design for UI-bearing scope |
| Primary consumer(s) | Observability, rollout, verification, and operations |

Own cloud/resources, Terraform or equivalent IaC, queues/topics, configuration, secrets wiring, feature flags, and delivery-pipeline changes required to run the feature.

## Activation and inputs

### Creation evidence

- Infrastructure, Terraform, cloud resource, queue/topic, pipeline, deployment, configuration, secret, feature flag, or environment evidence.

### Required inputs

- Approved architecture, resource ownership, environments, permissions, data classification, capacity, availability, deployment, and recovery requirements.

## Required activities

1. Implement reproducible infrastructure/configuration through the repository's governed mechanism.
2. Apply least privilege, encryption, isolation, naming/tagging, secret references, and environment separation.
3. Review capacity, cost, quotas, availability, dependencies, drift, and destructive changes.
4. Define deployment ordering, smoke checks, rollback/roll-forward, and operator ownership.
5. Run formatting, validation, policy, plan, and controlled deployment tests.

## Required outputs, dependencies, and authority

- Versioned infrastructure/configuration/pipeline artifacts.
- Reviewed plan and privilege/cost/capacity/recovery position.
- Validation, smoke, drift, and rollback evidence.

Only tasks without an unresolved dependency may run concurrently. Approved Visual
Design is a direct global dependency for every downstream UI-bearing task. Human
authority is required for scope changes, deferrals, residual-risk acceptance, and
any repository policy exception; task execution and evidence collection do not imply
that authority.

## Acceptance criteria

- [ ] Required resources and configuration are reproducible, least-privileged, environment-safe, and operable.
- [ ] Plans contain no unexplained destructive or privilege-expanding change.
- [ ] Deployment and recovery procedures have deterministic validation evidence.

## Negative criteria

- Do not commit secrets or use local implicit configuration as the deployment contract.
- Do not apply production changes as part of planning validation.
- Do not approve destructive changes without explicit human authority.

## Validation and completion evidence

- Run repository-specific format, validate, policy/security, plan, configuration, pipeline, smoke, drift, and rollback checks.
- Record tool/provider versions, environment, plan digest, commands, results, cost/destructive review, and approvals.

Completion evidence must identify the exact baseline, affected target, command or
review method, timestamp, actor/tool version, result, and artifact path or digest.
Open placeholders, unchecked acceptance/validation items, unexplained warnings, or
missing evidence prevent completion.

## Deferral and complexity

Any deferral records the unmet criterion, reason, owner, consequence, revisit
condition, residual risk, and human approval. Low for bounded configuration; medium for new resource/pipeline; high for destructive, privileged, multi-environment, or availability-critical change and must be decomposed.

## External issue hints

- Title: `Infrastructure and Deployment: <feature title>`.
- Labels: `cis`, `task-type:core.infrastructure.deployment`, plus affected repository/component labels.
- Body: canonical task link, source digest, targets, dependencies, gates, acceptance,
  validation, and evidence expectations.
- External status never grants CIS approval, accepts risk, or replaces the canonical
  Markdown task lifecycle.

---
title: "Task Type: Security and Permissions"
type: task-type-definition
status: Draft
version: "0.1"
scope: "Product:ChangeImpactStudio"
owner: "Andrew Spiteri"
last_reviewed: "2026-08-13"
review_cadence: "on planning-model change"
cis:
  stable_id: change-impact-studio:task-type:core.security.permissions
---

# Task Type: Security and Permissions

## Identity and boundary

| Property | Value |
| --- | --- |
| Stable type key | `core.security.permissions` |
| Provider | CIS Plan core module |
| Creation policy | Conditional |
| Required predecessor(s) | Documentation; approved Visual Design for UI-bearing scope |
| Primary consumer(s) | Data, API, backend, frontend, integration, infrastructure, verification, and assurance |

Own authentication, authorization, trust boundaries, sensitive-data handling, secrets, visibility, and prohibited exposure. It does not delegate security decisions implicitly to API or frontend work.

## Activation and inputs

### Creation evidence

- Authentication, authorization, permission, role, visibility, sensitive data, secret, exposure, abuse, or trust-boundary evidence.

### Required inputs

- Actor and permission model, data classification, threat/trust boundaries, decisions, and applicable security policies.
- Affected endpoints, screens, storage, events, logs, infrastructure, and negative requirements.

## Required activities

1. Define positive and denied access paths for every affected actor and state.
2. Enforce authorization at authoritative boundaries, not only in UI presentation.
3. Review sensitive fields, redaction, logging, secrets, transport, storage, and cache behavior.
4. Model abuse cases, confused-deputy paths, enumeration, replay, and privilege escalation proportional to risk.
5. Add deterministic negative tests and route significant residual risk to human review.
6. For unauthenticated endpoints, review public cache keys, representation variation,
   poisoning/enumeration risk, payload sensitivity, invalidation, and failure behavior.

## Required outputs, dependencies, and authority

- Policy/permission implementation and updated permission references.
- Negative access, exposure, redaction, and abuse-case tests.
- Security findings, disposition, and residual-risk record.

Only tasks without an unresolved dependency may run concurrently. Approved Visual
Design is a direct global dependency for every downstream UI-bearing task. Human
authority is required for scope changes, deferrals, residual-risk acceptance, and
any repository policy exception; task execution and evidence collection do not imply
that authority.

## Acceptance criteria

- [ ] Authorized actors can perform only approved operations and denied actors cannot infer or access restricted data.
- [ ] Secrets and sensitive data are not exposed through UI, API, logs, telemetry, errors, caches, or artifacts.
- [ ] Security regression checks pass with reviewed residual risk.
- [ ] `PUBLIC-ENDPOINT-CACHE`: public cache keys and payloads cannot mix or expose
      personalized, tenant-restricted, secret, sensitive, or incorrectly varied data.

## Negative criteria

- Do not rely on hidden controls as authorization.
- Do not broaden roles or permissions for implementation convenience.
- Do not record real secrets or sensitive production data as evidence.
- Do not permit an uncached unauthenticated endpoint or a public handler with direct
  database/repository access without an explicit human-approved architecture decision.

## Validation and completion evidence

- Run policy, endpoint, object-level authorization, redaction, secret scanning, and security regression checks.
- Record actor/path matrices, commands, results, findings, and approvals.

Completion evidence must identify the exact baseline, affected target, command or
review method, timestamp, actor/tool version, result, and artifact path or digest.
Open placeholders, unchecked acceptance/validation items, unexplained warnings, or
missing evidence prevent completion.

## Deferral and complexity

Any deferral records the unmet criterion, reason, owner, consequence, revisit
condition, residual risk, and human approval. Low for one established policy; medium for multiple actors/boundaries; high for new trust models, sensitive data, or cross-system authorization and must be decomposed.

## External issue hints

- Title: `Security and Permissions: <feature title>`.
- Labels: `cis`, `task-type:core.security.permissions`, plus affected repository/component labels.
- Body: canonical task link, source digest, targets, dependencies, gates, acceptance,
  validation, and evidence expectations.
- External status never grants CIS approval, accepts risk, or replaces the canonical
  Markdown task lifecycle.

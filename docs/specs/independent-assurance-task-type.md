---
title: "Task Type: Independent Assurance"
type: task-type-definition
status: Draft
version: "0.1"
scope: "Product:ChangeImpactStudio"
owner: "Andrew Spiteri"
last_reviewed: "2026-08-13"
review_cadence: "on planning-model change"
cis:
  stable_id: change-impact-studio:task-type:core.assurance.independent
---

# Task Type: Independent Assurance

## Identity and boundary

| Property | Value |
| --- | --- |
| Stable type key | `core.assurance.independent` |
| Provider | CIS Plan core module |
| Creation policy | Always |
| Required predecessor(s) | Verification |
| Primary consumer(s) | Final Delivery Sweep and Coordination |

Challenge implementation and verification with organizational or technical independence proportional to risk. It seeks evidence of false confidence rather than repeating the implementing agent's checklist.

## Activation and inputs

### Creation evidence

- Every governed feature creates this task; assurance techniques vary by risk.

### Required inputs

- Approved scope, implementation diff/artifacts, verification ledger, residual risks, architecture/security/accessibility policies, and risk classification.

## Required activities

1. Select independent techniques such as mutation testing, security review, architecture review, accessibility review, adversarial cases, or second-agent review.
2. Inspect whether verification could pass while prohibited behavior remains.
3. Record findings with severity, evidence, owner, required disposition, and retest criteria.
4. Require rework and renewed verification for blocking findings.
5. Separate reviewer identity/tool/run from the primary implementation claim.
6. For public endpoints, independently challenge cache bypass, poisoning, key
   variation, sensitive payload, stampede/failure behavior, and direct persistence
   dependency claims.

## Required outputs, dependencies, and authority

- Independent assurance report and finding ledger.
- Finding dispositions, retest evidence, and residual-risk recommendations.
- Statement of techniques applied and limitations.

Only tasks without an unresolved dependency may run concurrently. Approved Visual
Design is a direct global dependency for every downstream UI-bearing task. Human
authority is required for scope changes, deferrals, residual-risk acceptance, and
any repository policy exception; task execution and evidence collection do not imply
that authority.

## Acceptance criteria

- [ ] Proportionate independent techniques were executed against the actual change and verification evidence.
- [ ] Every finding has explicit resolved, accepted-risk, deferred, or non-applicable disposition with authority.
- [ ] No blocking finding or unexplained assurance gap remains.
- [ ] `PUBLIC-ENDPOINT-CACHE`: independent review confirms the public endpoint cannot
      bypass its cache or directly access a database/repository.

## Negative criteria

- Do not describe the implementer's own test run as independent assurance.
- Do not suppress findings to preserve a completion claim.
- Do not accept residual risk without identified human authority.

## Validation and completion evidence

- Run selected mutation/security/architecture/accessibility/adversarial/second-review checks and validate finding disposition completeness.
- Record reviewer/tool identity, baseline/diff, commands or review method, findings, evidence, dispositions, and limitations.

Completion evidence must identify the exact baseline, affected target, command or
review method, timestamp, actor/tool version, result, and artifact path or digest.
Open placeholders, unchecked acceptance/validation items, unexplained warnings, or
missing evidence prevent completion.

## Deferral and complexity

Any deferral records the unmet criterion, reason, owner, consequence, revisit
condition, residual risk, and human approval. Low for a focused independent review; medium for multiple techniques; high for critical security, architecture, accessibility, mutation, or regulated assurance and must be decomposed.

## External issue hints

- Title: `Independent Assurance: <feature title>`.
- Labels: `cis`, `task-type:core.assurance.independent`, plus affected repository/component labels.
- Body: canonical task link, source digest, targets, dependencies, gates, acceptance,
  validation, and evidence expectations.
- External status never grants CIS approval, accepts risk, or replaces the canonical
  Markdown task lifecycle.

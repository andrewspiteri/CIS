---
title: "Task Type: Rollout and Release"
type: task-type-definition
status: Draft
version: "0.1"
scope: "Product:ChangeImpactStudio"
owner: "Andrew Spiteri"
last_reviewed: "2026-08-13"
review_cadence: "on planning-model change"
cis:
  stable_id: change-impact-studio:task-type:core.release.rollout
---

# Task Type: Rollout and Release

## Identity and boundary

| Property | Value |
| --- | --- |
| Stable type key | `core.release.rollout` |
| Provider | CIS Plan core module |
| Creation policy | Conditional |
| Required predecessor(s) | Infrastructure and Observability where applicable; approved Visual Design for UI-bearing scope |
| Primary consumer(s) | Verification, assurance, support, and final delivery |

Own activation, staged exposure, compatibility windows, release communication, rollback criteria, and post-release validation without equating deployment with release.

## Activation and inputs

### Creation evidence

- Rollout, release, canary, staged activation, feature flag, compatibility window, rollback, release note, or post-release evidence.

### Required inputs

- Deployment artifacts, compatibility matrix, flags, target cohorts/environments, operational signals, risk, support readiness, and recovery position.

## Required activities

1. Define release units, order, owners, approvals, schedule constraints, and compatibility windows.
2. Specify flag lifecycle, cohort selection, canary stages, progression/stop criteria, and rollback triggers.
3. Prepare release notes and consumer/user/support communication.
4. Rehearse rollback or roll-forward and validate post-release signals/data.
5. Record actual stage decisions and outcomes without auto-advancing on ambiguous health.

## Required outputs, dependencies, and authority

- Reviewed release/rollout and rollback plan.
- Flag/cohort/compatibility inventory and communications.
- Stage, signal, post-release, and rollback evidence.

Only tasks without an unresolved dependency may run concurrently. Approved Visual
Design is a direct global dependency for every downstream UI-bearing task. Human
authority is required for scope changes, deferrals, residual-risk acceptance, and
any repository policy exception; task execution and evidence collection do not imply
that authority.

## Acceptance criteria

- [ ] Activation stages, ownership, success/stop/rollback criteria, compatibility, and communications are explicit.
- [ ] Each stage advances only on reviewed deterministic and operational evidence.
- [ ] Rollback/roll-forward and post-release validation are feasible and evidenced.

## Negative criteria

- Do not treat successful deployment as user release or feature acceptance.
- Do not leave temporary flags or compatibility paths ownerless.
- Do not auto-progress when signals are missing, stale, or ambiguous.

## Validation and completion evidence

- Run flag/default/cohort, compatibility, canary, rollback rehearsal, release-note, monitoring, and post-release data checks.
- Record stage, actor, timestamp, evidence, decision, outcome, and cleanup follow-up.

Completion evidence must identify the exact baseline, affected target, command or
review method, timestamp, actor/tool version, result, and artifact path or digest.
Open placeholders, unchecked acceptance/validation items, unexplained warnings, or
missing evidence prevent completion.

## Deferral and complexity

Any deferral records the unmet criterion, reason, owner, consequence, revisit
condition, residual risk, and human approval. Low for one internal activation; medium for staged cohorts/flags; high for public, multi-system, regulated, or long compatibility windows and must be decomposed.

## External issue hints

- Title: `Rollout and Release: <feature title>`.
- Labels: `cis`, `task-type:core.release.rollout`, plus affected repository/component labels.
- Body: canonical task link, source digest, targets, dependencies, gates, acceptance,
  validation, and evidence expectations.
- External status never grants CIS approval, accepts risk, or replaces the canonical
  Markdown task lifecycle.

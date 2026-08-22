---
name: cis-release-rollout
description: Plan and verify staged release, feature-flag, compatibility, rollback, and post-release checks. Use when a change affects deployment or published packages.
---

# CIS Release Rollout

## Purpose

Turn implementation evidence into a controlled and reversible release path.

## Workflow

1. Define artifacts, environments, compatibility window, and release ownership.
2. Specify flags, staged exposure, migration ordering, and rollback criteria.
3. Confirm observability and support readiness before increasing exposure.
4. Run post-release validation against acceptance and operational signals.
5. Record release evidence, incidents, rollback decisions, and follow-up work.

## Completion evidence

Preserve affected scope, exact commands, outcomes, generated artifacts, skipped checks, and residual risk in the canonical task or verification record.

## Guardrails

Do not infer production approval from plan or design approval.

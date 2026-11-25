---
title: "WORK-006 Attribute bounded invocation cost in the existing usage ledger"
type: agent-task
status: Draft
task_status: InProgress
task_id: WORK-006
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:3efd638a7aa741d9b736a91fb3789c60"
depends_on: []
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "change-impact-studio:CIS-0005:outcome:d0a7a7eb660641fbac2ba7b9b105b75c"
authorization_scope_ids: ["attribution"]
authorization_scope_basis: "sha256:a72c70dddd440bb623aea5f026e2c8776333941059b9e4fb9b477b326a32540c"
---

# WORK-006: Attribute bounded invocation cost in the existing usage ledger

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Attribute bounded invocation cost in the existing usage ledger

## Acceptance criteria

- [x] Add optional invocation/parent, parsed operation, safe change/task, bounded phases, graph consumption/freshness/reuse reasons, verification disposition, blocker codes and observable delivered-context sizes to existing bounded usage records. Preserve old fields/readability and unknown historical values; no secrets/raw environment or invented model token savings.

## Targeted validation

- [x] Focused Abstractions/Host/Feedback fixtures cover nested scope/operation attribution, bounded observations, legacy JSON roundtrips and reporting, secret-free code-only reasons, actual rendered sizes and no new material test inputs.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Implemented behavior and validation | Abstractions, Host and Feedback attribution/compatibility fixtures passed. Existing bounded usage records retain unknown historical values; model-token consumption and net savings remain unmeasured. Formal status is determined through normal CIS completion gates. | [Verification](../verification.md) |
## Source closeout execution context

The current implementation already exists from the authorized repair/bootstrap
work. Preserve that history and its reports. A native implement-mode read-only
run must genuinely inspect and adopt the current scoped implementation, identify
required corrections, and state its role accurately; it must not claim to have
authored historical bootstrap changes. Use the accepted criteria above and the
current source, tests and verification record as evidence.

Native verification is coordinated centrally under WORK-007. During these bounded
read-only implementation/review runs, inspect existing code and actual reports;
do not launch additional builds, tests, scanners or provider sub-runs. Never
substitute a reported claim for an unavailable or failed required check.

The independent native review must use a distinct run/session after implementation
inspection, report required versus optional findings with their basis, and assess
any material product or verification-machinery expansion under the existing
proportionate implementation policy. Read the actual current candidate and
retained reports, including scope limitations. Native source closeout evidence is
being assembled through the normal producers; the prior consumer proof remains
a dated observation, not a claim that every RM-08 criterion was proven.

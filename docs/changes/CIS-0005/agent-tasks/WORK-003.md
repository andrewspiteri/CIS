---
title: "WORK-003 Refresh current decisions and preserve conservative native selection and reuse"
type: agent-task
status: Draft
task_status: InProgress
task_id: WORK-003
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:48b46a20a050493a8680b76fa9280c02"
depends_on: []
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "change-impact-studio:CIS-0005:outcome:d0a7a7eb660641fbac2ba7b9b105b75c"
authorization_scope_ids: ["freshness"]
authorization_scope_basis: "sha256:4dfa492de16c5195536c83c123564a38e88a7656a38b4933816b04d798dabcc4"
---

# WORK-003: Refresh current decisions and preserve conservative native selection and reuse

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Refresh current decisions and preserve conservative native selection and reuse

## Acceptance criteria

- [x] Current impact and verification selection coalesce necessary refresh through existing orchestration, detect dirty add/edit/delete/dependency/interpretation changes, preserve historical navigation and approved baselines, expose scoped diagnostics and choose configured full fallback when dependency knowledge is uncertain. Unrelated documentation warnings retain eligible native evidence.

## Targeted validation

- [x] Existing graph/testing fixtures prove current freshness, conservative unknown-dependency fallback, unchanged second verification with zero extra native launches, relevant invalidation and unrelated documentation reuse. Retain existing report/authority/environment/settlement compatibility checks.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Implemented behavior and validation | Freshness, conservative fallback, material invalidation and documentation-reuse regressions passed. The installed unchanged second Verify passed with the same fingerprint/report and zero native launches. Formal status is determined through normal CIS completion gates. | [Verification](../verification.md) |
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

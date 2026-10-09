---
title: "Iteration Alignment and Completion Standard"
type: standard
status: Active
targets:
  - repository-governance
  - verification
owner: Repository maintainer
last_reviewed: 2026-08-15
review_cadence: on change
source_of_truth: This file
provenance:
  method: curated adaptation
  source_repository: ChangeImpactStudio
  source_paths:
    - docs/specs/iteration-alignment-plan.md
cis:
  stable_id: change-impact-studio:standard:iteration-completion
---

# Iteration Alignment and Completion Standard

## Evidence trust boundary

Local manifests, receipts and hashes detect stale or inconsistent evidence; they are not authenticated attestations. A process with repository write access can replace both a report and its hash. The assigned independent reviewer must inspect native execution records, source, scope and omissions rather than accept hand-written passing manifests. Explicit human implementation records identify authorship and do not grant governance approval. Provider sessions, implementation results and final source identities must agree with the retained execution evidence.

## Purpose

Refresh standards and evidence as the project grows, then close only verified current task scope.

This starter adapts the listed ChangeImpactStudio sources. Repository maintainers may strengthen it, record bounded exceptions, or supersede it through reviewed canonical changes.

## Scope

Applies to: repository-governance, verification.

## Normative language

`MUST` and `MUST NOT` are mandatory. `SHOULD` is the expected default and requires recorded rationale when not followed. `MAY` identifies an allowed option.

## Rules

- **ITERATION-001** Every completed task MUST have current graph, CIS dependency, all-standard and skill alignment assessment that preserves human-owned customizations and adopted alternatives.
- **ITERATION-002** The required gate inventory MUST be derived independently of configured successful commands; every applicable required gate MUST pass before completion.
- **ITERATION-003** Failed, missing, stale, skipped and justified inapplicable states MUST remain distinct; inapplicability MUST NOT be counted as a pass.
- **ITERATION-004** Completion MUST connect current scoped requirements, tasks, source, verification and review dispositions; later phases MUST remain visible without granting operational activation.
- **ITERATION-005** Implementation and review MUST inspect authoritative source when graph or summary context is incomplete or inaccurate, and MUST refresh affected context after intervening edits.
- **ITERATION-006** Local evidence hashes MUST NOT be treated as authenticated attestations. The assigned reviewer MUST inspect native execution provenance, implementation authorship and material evidence omissions.

## Verification

- `ITERATION-001`: Inspect reconciliation preview, final graph identity and task closing evidence.
- `ITERATION-002`: Deliberately omit a required gate and verify that completion fails.
- `ITERATION-003`: Exercise each gate state with scoped evidence and verify transition behavior.
- `ITERATION-004`: Review exact scoped requirement IDs and unresolved decisions/findings.
- `ITERATION-005`: Exercise missing-caller, misleading-summary and changed-input cases.
- `ITERATION-006`: Verify the retained implementation result and separate reviewer session; reject hand-written passing manifests as execution proof.

Run `cis standards validate --strict` after changing this standard or its conformance mappings.

## Exceptions

An exception requires the rule ID, human approver, rationale, bounded scope, review or expiry condition, and compensating controls. CIS and agents cannot approve exceptions.

The separate `.cis/engineering-adoption.json` record preserves adoption when starter ownership metadata is removed. Removing the starter entry and disabling the policy still blocks. This is an additional consistency check, not an authenticated history: rewriting every local governance record is outside the protection supplied by local hashes. CIS does not provide an automatic policy-withdrawal or exception-approval command; any proposed withdrawal requires an explicit maintainer decision and independent governance review.

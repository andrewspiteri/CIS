---
name: cis-tooling-evidence
description: Record and validate exact CIS routing, generation, testing, run, and usage evidence for changed files. Use before handoff, verification, or pull-request readiness checks.
---

# CIS tooling evidence

## Purpose

Keep exact evidence useful and small enough to review, without overstating what ran.

## Workflow

1. Add a `## CIS tooling evidence` section to the handoff. Link existing artifacts
   rather than copying them into each task.
2. Record applicable context/graph commands, generation decision, reconciled test
   command, run ID, source commit and relevant dirty diff, configuration/tool
   versions, outcome and hashes. State skipped or unavailable checks explicitly.
3. For mutation work, keep the unchanged native report and a small summary separating
   native outcomes, conservative assessment and execution failures. Count distinct
   mutants, not diagnostic rows. Do not add bespoke per-mutant proof requirements
   where ordinary native results meet the adopted policy.
4. Retain the local usage ledger or state a specific bypass reason. Bound additional
   diagnostics to a concrete failure and retention budget; full source/runtime copies
   and repeated archives are not default completion evidence. Preserve stronger
   repository requirements and never silently discard protected evidence.
5. Run `cis agent evidence validate --evidence <path> --changed-file <path> --strict --format agent`.
6. Replace placeholders and resolve every warning before readiness is claimed.

## Guardrails

A hash or summary does not prove an unrun check or preserve unavailable dirty source.
Keep the small relevant diff when reproduction needs it. Evidence-budget exhaustion
is a blocker to resolve, not permission to drop failures, archive indefinitely or
relax a gate.

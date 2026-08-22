---
name: cis-add-mutation-tests
description: Use mutation testing to challenge important unit-test assertions. Use when risk or independent assurance requires evidence beyond line coverage.
---

# CIS Add Mutation Tests

## Purpose

Find behavior that existing tests execute but do not meaningfully assert.

## Workflow

1. Select a bounded production scope and relevant test project.
2. Reuse repository mutation configuration and baselines.
3. Run the narrowest mutation job practical for the risk.
4. Classify surviving mutants as test gaps, equivalent mutants, or accepted limitations.
5. Preserve the report path and disposition evidence.

## Completion evidence

Preserve affected scope, exact commands, outcomes, generated artifacts, skipped checks, and residual risk in the canonical task or verification record.

## Guardrails

Do not run an unbounded mutation suite by default or hide surviving mutants.

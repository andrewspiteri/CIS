---
name: cis-add-business-acceptance-tests
description: Add acceptance-style tests for cross-step business outcomes and lifecycle workflows. Use when behavior spans commands, states, actors, or repositories.
---

# CIS Add Business Acceptance Tests

## Purpose

Prove reviewed acceptance behavior in domain language without turning scenarios into low-level implementation scripts.

## Workflow

1. Select approved acceptance criteria that cross more than one operation or state transition.
2. Express scenarios using actors, preconditions, actions, outcomes, and denied paths.
3. Keep step or fixture code thin and delegate setup through supported application boundaries.
4. Use deterministic identities, clocks, and data while avoiding live human authentication.
5. Run the focused acceptance suite and preserve scenario-level evidence.

## Completion evidence

Preserve affected scope, exact commands, outcomes, generated artifacts, skipped checks, and residual risk in the canonical task or verification record.

## Guardrails

Do not replace focused unit or integration tests with broad acceptance scenarios.

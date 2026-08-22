---
name: cis-add-unit-tests
description: Add focused unit tests that prove behavior and edge cases without coupling to implementation details. Use when changing deterministic logic.
---

# CIS Add Unit Tests

## Purpose

Create fast tests that explain the contract and fail for meaningful regressions.

## Workflow

1. Map acceptance criteria and invariants to observable cases.
2. Follow the repository test framework, naming, and fixture conventions.
3. Cover success, boundary, invalid, and authorization-sensitive behavior as applicable.
4. Run the smallest affected test selection and inspect the failure mode.
5. Record test evidence in the task or verification record.

## Completion evidence

Preserve affected scope, exact commands, outcomes, generated artifacts, skipped checks, and residual risk in the canonical task or verification record.

## Guardrails

Do not add tautological tests, sleep-based synchronization, or assertions on private implementation structure.

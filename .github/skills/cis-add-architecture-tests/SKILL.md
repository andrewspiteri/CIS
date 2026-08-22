---
name: cis-add-architecture-tests
description: Add deterministic architecture tests for module boundaries and dependency rules. Use when a .NET change creates or crosses architectural seams.
---

# CIS Add Architecture Tests

## Purpose

Protect intended dependencies and ownership boundaries as executable policy.

## Workflow

1. Read ADRs, module ownership, and existing architecture-test conventions.
2. Express one stable boundary rule per test.
3. Include a deliberate negative fixture when the framework permits it.
4. Run the architecture-test project and preserve the command output.
5. Link the rule to its governing decision or specification.

## Completion evidence

Preserve affected scope, exact commands, outcomes, generated artifacts, skipped checks, and residual risk in the canonical task or verification record.

## Guardrails

Do not encode transient namespaces as architecture without a canonical rule.

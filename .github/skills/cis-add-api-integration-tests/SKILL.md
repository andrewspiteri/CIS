---
name: cis-add-api-integration-tests
description: Add API integration tests for contracts, authorization, persistence boundaries, and errors. Use when changing a backend endpoint or API behavior.
---

# CIS Add Api Integration Tests

## Purpose

Prove the externally observable API contract against the real application boundary.

## Workflow

1. Start from the API dictionary, OpenAPI contract, permissions, and problem catalogue.
2. Exercise success, validation, missing-resource, conflict, and authorization cases as applicable.
3. Use the repository application factory or supported integration harness.
4. Assert status, headers, schema, important values, and prohibited disclosure.
5. Run deterministic contract validation with the targeted integration tests.

## Completion evidence

Preserve affected scope, exact commands, outcomes, generated artifacts, skipped checks, and residual risk in the canonical task or verification record.

## Guardrails

Do not bypass middleware or substitute controller-unit tests for API integration evidence.

---
name: cis-secure-feature
description: Threat-model and verify authorization, exposure, validation, and secret-handling changes. Use when a feature crosses a trust boundary or handles sensitive data.
---

# CIS Secure Feature

## Purpose

Make security requirements explicit and testable before completion is claimed.

## Workflow

1. Read `.github/instructions/cis-engineering-assurance.instructions.md`, the applicable security standard and suite profile.
2. Identify actors, principal and tenant scope, assets, trust boundaries, entry points and abuse cases.
3. Map permissions to server-side enforcement and negative tests. Isolate test authentication from production.
4. Check validation, output filtering, logging, secrets, caching and rate limits.
5. Maintain real configuration and environment contracts without inventing entries for pure fixtures.
6. For public endpoints, verify cached projection access and no direct database dependency.
7. Preserve required CodeQL or other SAST, dependency, secret and DAST checks and exact human-approved exception rules.
8. Update tests and documentation together, then record security evidence, residual risk and independent review requirements.

## Completion evidence

Preserve affected scope, exact commands, outcomes, generated artifacts, skipped checks, and residual risk in the canonical task or verification record.

## Guardrails

Do not rely on hidden UI controls or client-side checks as authorization.

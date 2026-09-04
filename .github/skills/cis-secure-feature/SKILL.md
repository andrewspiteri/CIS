---
name: cis-secure-feature
description: Threat-model and verify authorization, exposure, validation, and secret-handling changes. Use when a feature crosses a trust boundary or handles sensitive data.
---

# CIS Secure Feature

## Purpose

Make security requirements explicit and testable before completion is claimed.

## Workflow

1. Identify actors, assets, trust boundaries, entry points, and abuse cases.
2. Map permissions to server-side enforcement and negative tests.
3. Check validation, output filtering, logging, secrets, caching, and rate limits.
4. For public endpoints, verify cached projection access and no direct database dependency.
5. Record security evidence, residual risk, and independent review requirements.

## Completion evidence

Preserve affected scope, exact commands, outcomes, generated artifacts, skipped checks, and residual risk in the canonical task or verification record.

## Guardrails

Do not rely on hidden UI controls or client-side checks as authorization.

---
name: cis-dependency-upgrades
description: Upgrade dependencies in bounded batches with compatibility, security, and verification evidence. Use when package versions or lockfiles change.
---

# CIS Dependency Upgrades

## Purpose

Reduce dependency risk without mixing unrelated upgrades or obscuring generated changes.

## Workflow

1. Classify the upgrade as security, required compatibility, maintenance, or optional.
2. Read release notes and identify breaking, runtime, license, and transitive changes.
3. Update manifests and lockfiles with the repository package manager.
4. Run affected build, tests, analyzers, and packaging checks.
5. Record deferred upgrades and residual compatibility risk.

## Completion evidence

Preserve affected scope, exact commands, outcomes, generated artifacts, skipped checks, and residual risk in the canonical task or verification record.

## Guardrails

Do not bundle unrelated major upgrades into feature work without explicit scope.

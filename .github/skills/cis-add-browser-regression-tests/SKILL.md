---
name: cis-add-browser-regression-tests
description: Add stable browser regression tests for approved user journeys and visual states. Use when web frontend behavior or rendering changes.
---

# CIS Add Browser Regression Tests

## Purpose

Prove high-value browser journeys while keeping selectors and visual evidence maintainable.

## Workflow

1. Read .github/instructions/cis-engineering-assurance.instructions.md and the repository's approved browser harness; preserve .NET Playwright or Node Playwright as adopted.
2. Start from approved wireframes, design manifests, routes, and acceptance criteria.
3. Reuse deterministic app composition, isolated seeded data, page objects and production-isolated test authentication.
4. Use semantic selectors and observable readiness rather than sleeps or unconditional network-idle waits.
5. Exercise changed actions through their resulting mutations, saved values, navigation and error states; presence alone is not coverage.
6. Cover navigation, loading, empty, error, authorization, disabled, keyboard and responsive states as applicable.
7. Keep visual snapshots bounded to reviewed components or screens.
8. Run the supported browser matrix in its correct CI tier, preserve failure artifacts and explicitly report unavailable prerequisites.

## Completion evidence

Preserve affected scope, exact commands, outcomes, generated artifacts, skipped checks, and residual risk in the canonical task or verification record.

## Guardrails

Do not update snapshots merely to make an unexplained difference pass.

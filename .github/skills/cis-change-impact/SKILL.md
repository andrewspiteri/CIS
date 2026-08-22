---
name: cis-change-impact
description: Orchestrate CIS change preparation from exact-baseline dossier creation through graph context, reviewed impact, decisions, and validated bounded planning. Use for features, fixes, refactors, dependency/configuration/schema/API/workflow changes, or any request that must be assessed before implementation.
---

# CIS Change Impact

## Workflow

1. Read `.cis/repository.yml`, the repository profile, catalog, and applicable specifications.
2. Use `cis-graph-context` to ensure a fresh graph and identify exact roots.
3. Use `cis-change-dossier` to create or inspect the exact-baseline change record.
4. Use `cis-impact-review` to analyse and present findings for human disposition.
5. Use `cis-decision-review` for unresolved blocking or advisory questions.
6. Use `cis-bounded-planning` to build and validate work after impact review.
7. Recheck planned versus actual scope with `cis-validate-completion` before closure.

## Guardrails

Never infer finding disposition, decision resolution, plan approval, completion,
or closure. Run authority-changing commands only after the user explicitly
authorizes the exact record and rationale.

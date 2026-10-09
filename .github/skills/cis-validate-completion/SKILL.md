---
name: cis-validate-completion
description: Validate completion claims against requested scope, acceptance criteria, documentation co-changes, deterministic checks, and residual risk.
---

# CIS Validate Completion

## Purpose

Prevent partial or weakly evidenced work from being presented as complete.

## When to Use

Use before handoff, review readiness, release acceptance, or closing a change.

## Inputs

Gather the request, plan, acceptance criteria, changed files, documentation delta, validation results, and unresolved risks.

## Workflow

1. Confirm the intended output exists and matches scope.
2. Check every acceptance criterion against direct evidence.
3. Confirm required specifications and references changed with behavior.
4. Verify deterministic checks and independent assurance requirements.
5. Rebuild the affected graph, preview CIS dependency and all-standard/skill reconciliation, preserve customizations and apply authorized updates. Refresh affected evidence after changes; verify context freshness before the next task.
6. Inventory required gates independently of configured commands. Failed, missing, stale and skipped block completion; justify inapplicability separately. Check scoped requirement-to-task-to-source-to-test-to-review traceability and unresolved findings.
7. Have the assigned independent reviewer inspect final source, callers/contracts, operational effects, context omissions and closing evidence. Expand misleading summaries to source. A fresh graph does not establish sufficient context by itself.
8. For adopted engineering defaults, use `cis plan task completion-context` to prepare the missing-state receipt. Request each declared target with `--target <repository-id>` for multi-target tasks and save the receipt at the returned authority-relative `receiptPath`; the transition checks every target. Native implementation/review records stay in the authority's run directories, while other gate artifacts are relative to the target. Verify current authority context as well as participant source and checks. Report a verified verdict and exact remaining work; the template never certifies execution.

## Output Expectations

Produce an auditable verdict with evidence, limitations, blockers, and next action.

## Guardrails

Do not equate drafting, compilation, or self-review with completion.

## Related Files

Read `docs/specs/delivery-and-assurance-spec.md` and the active change record.

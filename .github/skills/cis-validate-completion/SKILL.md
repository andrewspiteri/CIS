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

Gather the shared authorized outcome and limits, the task-specific plan and acceptance criteria, changed files, documentation delta, validation results, and unresolved risks.

## Workflow

1. Confirm the intended output exists and matches accepted behavior, ownership and exclusions under the canonical code-quality standard. Inspect actual source/callers and the existing review's necessity conclusion; passing tests do not justify unsupported responsibilities. Ordinary helpers need no extra document or human checkpoint.
2. Check every acceptance criterion against direct evidence.
3. Confirm required specifications and references changed with behavior.
4. Verify deterministic checks and independent assurance requirements.
5. Retain current applicable graph, CIS dependency, standards, skill, review and verification assessments when their material basis is unchanged. Refresh only affected or unresolved portions, preserve human customizations and adopted alternatives, and apply authorized updates. Rebuild an affected stale graph or preview relevant reconciliation where needed; an unrelated edit or bookkeeping update alone does not require repeating unaffected checks.
6. Inventory required gates independently of configured commands. Failed, missing, stale and skipped applicable required gates block completion; justify inapplicability separately. Check scoped requirement-to-task-to-source-to-test-to-review traceability and resolve required findings. Record an honest disposition for optional advice without a compulsory correction loop.
7. Use the assigned independent reviewer for final source, callers/contracts, operational effects, context omissions and closing evidence. Retain compatible review evidence; repeat review for materially changed scope or an unmet applicable review gate. Expand misleading summaries to source. A fresh graph does not establish sufficient context by itself.
8. For adopted engineering defaults, use `cis plan task completion-context` to prepare the missing-state receipt. Request each declared target with `--target <repository-id>` for multi-target tasks and save the receipt at the returned authority-relative `receiptPath`; the transition checks every target. Native implementation/review records stay in the authority's run directories, while other gate artifacts are relative to the target. Verify current authority context as well as participant source and checks. Report a verified verdict and exact remaining work; the template never certifies execution.

Use the normal checked task transition under existing outcome authority; completion needs no separate human decision per task. Record only task-specific evidence and reference shared scope and limits. Accepted no-UI work and work without a manual-catalogue obligation need no irrelevant templates. Preserve historical full dossiers and real design/publication decisions. Ask only for a specific difference outside authority, while unrelated eligible work and unchanged valid results remain usable.

## Output Expectations

Produce an auditable verdict with evidence, limitations, blockers, and next action.

## Guardrails

Do not equate drafting, compilation, or self-review with completion.

## Related Files

Read `docs/specs/delivery-and-assurance-spec.md` and the active change record.

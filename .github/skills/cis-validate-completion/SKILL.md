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
2. Apply the promised-workflow checks below to every acceptance criterion and its direct evidence.
3. Confirm required specifications and references changed with behavior.
4. Verify deterministic checks and independent assurance requirements.
5. Retain current applicable graph, CIS dependency, standards, skill, review and verification assessments when their material basis is unchanged. Refresh only affected or unresolved portions, preserve human customizations and adopted alternatives, and apply authorized updates. Rebuild an affected stale graph or preview relevant reconciliation where needed; an unrelated edit or bookkeeping update alone does not require repeating unaffected checks.
6. Inventory required gates independently of configured commands. Failed, missing, stale and skipped applicable required gates block completion; justify inapplicability separately. Check scoped requirement-to-task-to-source-to-test-to-review traceability and resolve required findings. Record an honest disposition for optional advice without a compulsory correction loop.
7. Use the assigned independent reviewer for final source, callers/contracts, operational effects, context omissions and closing evidence. Apply the same promised-workflow checks during required corrections; optional advice creates no compulsory work, and correction-round limits are ceilings, not quotas. Retain compatible review evidence; repeat review for materially changed scope or an unmet applicable review gate. Expand misleading summaries to source. A fresh graph does not establish sufficient context by itself.
8. For adopted engineering defaults, use `cis plan task completion-context` to prepare the missing-state receipt. Request each declared target with `--target <repository-id>` for multi-target tasks and save the receipt at the returned authority-relative `receiptPath`; the transition checks every target. Native implementation/review records stay in the authority's run directories, while other gate artifacts are relative to the target. Verify current authority context as well as participant source and checks. Report a verified verdict and exact remaining work; the template never certifies execution.

Use the normal checked task transition under existing outcome authority; completion needs no separate human decision per task. Record only task-specific evidence and reference shared scope and limits. Accepted no-UI work and work without a manual-catalogue obligation need no irrelevant templates. Preserve historical full dossiers and real design/publication decisions. Ask only for a specific difference outside authority, while unrelated eligible work and unchanged valid results remain usable.

## Promised workflow checks

Derive expected behavior and checks from the accepted outcome and concrete acceptance
before relying on the implementation's tests. Check the operation the user will
actually perform, the existing state it must preserve and the next operation that
must remain possible. Use a supported service/controller or other real entry point
when a helper test cannot establish that promise. Trace relevant callers and consumers;
for changed shared state, inspect writers, readers, validators and terminal/recovery
transitions. Exercise alternate paths where their semantics materially differ within
the claimed scope.

Check required denial and legitimate resolution or continuation. A hold test does not
prove that the workflow can finish. For an amendment or recovery path, follow it through
its supported resolution and next operation. Derive expected results independently of
the implementation; do not mock away the rule being checked or infer representative
inputs from a fixture's name. State exactly what actual checks establish and what they
leave unverified. Keep concise links between acceptance, actual paths and meaningful
checks in the existing review record, without another matrix, dossier or review stage.

For example, the amended-scope promise needs historical disposition followed by a
current replacement; stale-work rejection alone is incomplete. A native execution
fixture does not establish participant completion/resume behavior.

## Defects after a reviewed handoff

When a required defect escapes a reviewed handoff, briefly record the ordinary
promised scenario missed, why the original check did not expose it and which existing
regression or instruction now covers the cause. Inspect the other consumers of the
affected rule/state and a relevant adjacent transition within the same outcome. Use
the existing correction or verification record, not a learning task or evidence archive.

Fixes caught during the initial internal review are normal corrections, not escaped
handoff defects. A declared ReadyForReview checkpoint is not formal acceptance.
Report pre-existing closing gates and the intended handoff separately; a focused
correction does not imply full dossier acceptance or release qualification.

## Output Expectations

Produce an auditable verdict with evidence, limitations, blockers, and next action.

## Guardrails

Do not equate drafting, compilation, or self-review with completion.

## Related Files

Read `docs/specs/delivery-and-assurance-spec.md` and the active change record.

---
name: cis-verification
description: Plan and execute proportionate deterministic verification, preserve evidence, and report limitations without overstating completion.
---

# CIS Verification

## Purpose

Make completion claims auditable and proportionate to change risk.

## When to Use

Use before implementation planning, after meaningful changes, and before handoff or release acceptance.

## Inputs

Gather affected components, contracts, risks, acceptance criteria, available tooling, and required assurance independence.

## Workflow

1. Run `cis test inventory` and `cis test validate --strict`; reconcile the canonical suite profile before execution.
2. During implementation, run the smallest relevant planned checks that can fail for
   the changed behavior. Expand according to impact and failures; a tiny iteration
   does not automatically require the whole standard-delivery workflow. Preserve any
   stronger active requirement and run `cis workflow run standard-delivery --run-id <id>`
   at the required handoff/checkpoint. Full required release gates remain mandatory.
   Canonical retries are zero and every attempt remains visible.
   Observe each gate separately or use fail-fast orchestration that preserves each
   exit status; a later successful command must not mask an earlier failure.
3. Inspect failure classification before a diagnostic rerun. Infrastructure,
   prerequisite, timeout, cancellation and unknown failures are not product passes.
   Give diagnostics a fixed attempt/time/storage budget; repeated identical runs
   without a new hypothesis or changed evidence are a stop condition.
4. Run `cis test reconcile --run <id>` and `cis test trace <change-id> --run <id>`. Missing or unreadable expected evidence is `invalid-evidence` even after exit code zero.
5. Record exact unavailable checks and bounded human-approved exceptions; never silently pass them.
6. Keep independent assurance separate from the implementer or use a genuinely
   independent mechanical technique. Record actors, technique, run ID, profile
   digest, source revision/dirty diff and artifact hashes. Link native reports and
   small summaries; scope extra captures to a concrete failure and retention budget.
   For mutation, qualify the native runner with a small healthy/failing canary before
   broad execution, including real children when applicable. Keep native outcomes,
   conservative interpretation and execution health distinct; only the complete
   final required scope can satisfy its gate.
7. After evidence stabilizes, run `cis verify diff`. Confirm the snapshot names every
   planned repository, includes tracked and untracked changes, and is non-empty.
   Then run `cis verify validate`.
8. Only on explicit human acceptance use `cis verify finalize --reviewer <human> --reason <rationale>`.

## Output Expectations

Report a clear verdict, exact evidence, unrun checks, blockers, and residual risk.

## Guardrails

Do not claim checks that were not run, infer earlier success from the final exit code
of a non-fail-fast command chain, accept an empty or stale snapshot, or equate
compilation with complete verification. Focused iteration evidence is not full
delivery or release acceptance. Preserve thresholds, failures and stronger policy;
do not add a blanket every-row direct-assertion rule to ordinary native mutation
results. Storage caps are real limits, not an invitation to retain endless archives.

## Related Files

Read the delivery-and-assurance specification and `.github/skills/cis-validate-completion/SKILL.md`.

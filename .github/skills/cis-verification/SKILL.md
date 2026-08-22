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

1. Start with targeted build, test, lint, analysis, security, and packaging checks.
2. Run each evidence gate as an independently observed command, or use fail-fast orchestration that preserves every gate's exit status; never let a later successful command mask an earlier failure.
3. Expand to component or repository checks only when impact or failures justify it.
4. Record commands, outcomes, skipped checks, environmental limits, and residual risk.
5. Keep independent assurance separate from the implementing agent's self-assessment.
6. After all implementation and evidence are stable, run `cis verify diff` and confirm the snapshot names every planned repository, includes tracked and untracked changes, and is non-empty.
7. Run `cis verify validate`. If the human explicitly accepts the outcome, use `cis verify finalize --reviewer <human> --reason <rationale>` so lifecycle completion, closure, recapture, and acceptance remain coordinated.

## Output Expectations

Report a clear verdict, exact evidence, unrun checks, blockers, and residual risk.

## Guardrails

Do not claim checks that were not run, infer earlier success from the final exit code of a non-fail-fast command chain, accept an empty or stale snapshot, or equate compilation with complete verification.

## Related Files

Read the delivery-and-assurance specification and `.github/skills/cis-validate-completion/SKILL.md`.

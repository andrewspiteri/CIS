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
2. Execute `cis workflow run standard-delivery --run-id <id>`. Canonical retries are zero and every attempt remains visible.
3. Inspect failure classification before a diagnostic rerun. Infrastructure, prerequisite, timeout, cancellation, and unknown failures are not product passes.
4. Run `cis test reconcile --run <id>` and `cis test trace <change-id> --run <id>`. Missing or unreadable expected evidence is `invalid-evidence` even after exit code zero.
5. Record exact unavailable checks and bounded human-approved exceptions; never silently pass them.
6. Keep independent assurance separate from the implementer or use a genuinely independent mechanical technique. Record actors, technique, run ID, profile digest, repository revision, and artifact hashes.
7. After evidence stabilizes, run `cis verify diff`, then `cis verify validate`.
8. Only on explicit human acceptance use `cis verify finalize --reviewer <human> --reason <rationale>`.

## Output Expectations

Report a clear verdict, exact evidence, unrun checks, blockers, and residual risk.

## Guardrails

Do not claim checks that were not run, infer earlier success from the final exit code of a non-fail-fast command chain, accept an empty or stale snapshot, or equate compilation with complete verification.

## Related Files

Read the delivery-and-assurance specification and `.github/skills/cis-validate-completion/SKILL.md`.

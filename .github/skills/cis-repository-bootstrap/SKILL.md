---
name: cis-repository-bootstrap
description: Initialize or reconcile CIS in a target repository, respect confirmation gates, and run repository doctor after initialization errors or collisions.
---

# CIS Repository Bootstrap

## Purpose

Establish or reconcile CIS repository state without guessing the documentation root or bypassing human review.

## When to Use

Use when onboarding a repository, adding projects to an initialized repository, upgrading CIS starters, or recovering from a failed `cis repo init` run.

## Inputs

Obtain the target repository path and an explicit repository-relative documentation root chosen by the maintainer.

## Workflow

1. Run `cis repo init --repo <repository> --root <documentation-root> --dry-run --format agent`.
2. Review classification evidence, planned creates and updates, warnings, and collisions.
3. Treat exit code `3` as a confirmation gate, not a failure. After maintainer review and authorization, rerun with `--yes`.
4. If init returns exit code `2`, exit code `4`, an error, or a collision, run `cis repo doctor --repo <repository> --root <documentation-root> --format agent` immediately.
5. Use doctor evidence and suggested fixes to explain the blocker. Apply no suggested fix without the required human review.
6. Rerun init after the blocker is resolved and confirm that the final plan is applied or unchanged.

## Output Expectations

Report the chosen root, init status and exit code, doctor findings when init failed, applied changes, unresolved collisions, and the exact next action.

## Guardrails

Do not choose a documentation root silently, use `--yes` before reviewing a non-empty root, overwrite collisions, execute target-repository assemblies, or treat doctor suggestions as automatic authorization.

## Related Files

Read `.cis/repository.yml` when it exists and use the `cis repo init` and `cis repo doctor` command manuals from the CIS distribution.

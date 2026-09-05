---
name: cis-repository-bootstrap
description: Create CIS for a new empty project or import an existing repository, respect confirmation gates, and run repository doctor after onboarding errors or collisions.
---

# CIS Repository Bootstrap

## Purpose

Establish or reconcile CIS repository state without guessing the documentation root or bypassing human review.

## When to Use

Use when onboarding a new or existing repository, adding projects to an initialized repository, upgrading CIS starters, or recovering from a failed create or import run.

## Inputs

Obtain the target repository path and an explicit repository-relative documentation root chosen by the maintainer.

## Workflow

1. If project manifests or implementation source already exist, dry-run `cis repo import --workspace <repository> --source <repository> --root <documentation-root> --participation owned --relationship none --ecosystem <id> --product <id>`. For a genuinely empty project, dry-run `cis repo init --repo <repository> --root <documentation-root>`, then create the product authority with `cis workspace init --root <documentation-root> --ecosystem <id> --product <id>`.
2. Review the selected create/import mode, classification evidence, planned creates and updates, warnings, and collisions.
3. Treat exit code `3` as a confirmation gate, not a failure. After maintainer review and authorization, rerun with `--yes`.
4. If create or import returns exit code `2`, exit code `4`, an error, or a collision, run `cis repo doctor --repo <repository> --root <documentation-root> --format agent` immediately.
5. Use doctor evidence and suggested fixes to explain the blocker. Apply no suggested fix without the required human review.
6. Rerun the same onboarding command after the blocker is resolved and confirm that the final plan is applied or unchanged.

## Output Expectations

Report the chosen root, create/import mode, status and exit code, doctor findings when onboarding failed, applied changes, unresolved collisions, and the exact next action.

## Guardrails

Do not choose a documentation root silently, use `--yes` before reviewing a non-empty root, overwrite collisions, execute target-repository assemblies, or treat doctor suggestions as automatic authorization.

## Related Files

Read `.cis/repository.yml` when it exists and use the `cis repo init`, `cis repo import`, and `cis repo doctor` command manuals from the CIS distribution.

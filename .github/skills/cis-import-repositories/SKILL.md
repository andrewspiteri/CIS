---
name: cis-import-repositories
description: Import, initialize, register, list, graph, or validate one or several existing repositories as a CIS workspace. Use for standalone or multi-repository onboarding and repeatable workspace reconciliation without copying source repositories.
---

# Import CIS Repositories

## Inputs

Obtain the workspace path, every source repository path, and one explicit repository-relative documentation root approved for the import batch.

## Workflow

1. Run `cis repo import --workspace <workspace> --source <repository>... --root <documentation-root> --dry-run --format agent`. For a standalone existing repository, use that same path for workspace and source so import bootstraps it as authority.
2. Review every classification, planned initialization change, warning, collision, and workspace registry entry.
3. If any repository cannot initialize, run `cis repo doctor` for that repository with the same root and report the evidence before retrying the batch.
4. After explicit authorization, repeat import with `--yes`; never add `--yes` to the first run.
5. Run `cis repo list --workspace <workspace> --format agent` and verify all expected repository IDs and paths.
6. Run `cis graph build --workspace <workspace> --format agent`, followed by `cis graph validate --workspace <workspace> --format agent`.
7. Use registered repository paths as explicit roots when producing federated context packs. Treat each local graph identity, freshness, diagnostics, and omissions independently.

## Guardrails

Treat import as registration and initialization, not source copying. Do not silently choose a documentation root, import more than 20 repositories in one batch, hand-edit derived `.cis/local/` graph state, invent cross-repository edges, or hide a partial repository result behind an aggregate success claim.

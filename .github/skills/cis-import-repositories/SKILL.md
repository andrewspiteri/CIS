---
name: cis-import-repositories
description: Import, initialize, register, list, graph, or validate one or several existing repositories as a CIS workspace. Use for standalone or multi-repository onboarding and repeatable workspace reconciliation without copying source repositories.
---

# Import CIS Repositories

## Inputs

Obtain the workspace path, every source repository path, one explicit repository-relative
documentation root, the governed product and ecosystem identities, and the requested
participation boundary. Owned imports use relationship `none`. Dependencies require
producer, consumer, or bidirectional direction and may name bounded components.

## Workflow

1. Run `cis repo import --workspace <workspace> --source <repository>... --root <documentation-root> --participation <owned|dependency> --relationship <none|producer|consumer|bidirectional> --dry-run --format agent`. For a standalone existing repository, use that same path for workspace and source and include `--ecosystem <id> --product <id>` so import bootstraps it as authority.
2. Review every classification, planned initialization change, warning, collision, and workspace registry entry.
3. If any repository cannot initialize, run `cis repo doctor` for that repository with the same root and report the evidence before retrying the batch.
4. After explicit authorization, repeat import with `--yes`; never add `--yes` to the first run.
5. Run `cis repo list --workspace <workspace> --format agent` and verify product, ecosystem, repository IDs, paths, participation, direction, and component scope.
6. Run `cis graph build --workspace <workspace> --format agent`, followed by `cis graph status --workspace <workspace> --format agent`. Use `cis graph validate --workspace <workspace>` at an assurance gate, not as a routine freshness probe.
7. Use registered repository paths as explicit roots when producing federated context packs. Treat each local graph identity, freshness, diagnostics, and omissions independently.

## Guardrails

Treat import as registration and initialization, not source copying. Never infer product
ownership. Do not route product implementation tasks or workspace-write agents to a
dependency; create a separately governed change under its owning product workspace. Do
not silently choose a documentation root, import more than 20 repositories in one batch,
hand-edit derived `.cis/local/` graph state, invent cross-repository edges, or hide a
partial repository result behind an aggregate success claim.

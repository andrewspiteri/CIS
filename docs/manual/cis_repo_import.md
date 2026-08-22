---
title: "cis repo import"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-repo-import
---

# `cis repo import`

Initializes and registers between one and twenty existing repositories in a CIS
workspace. Import records repository locations; it does not copy, move, clone, or
execute source repositories.

## Synopsis

```text
cis repo import --source <path> [<path>...] --root <repository-relative-path>
  [--workspace <path>] [--dry-run] [--yes]
  [--format <human|json|agent>]
```

## Workflow

The command resolves and deduplicates every source path, plans `cis repo init` for the
complete batch, and rejects the batch before mutation when any source is invalid or has
an initialization collision. It then merges the repositories into
`.cis/workspace.yml`. Registry paths are stored relative to the workspace when
possible.

Run `cis workspace init` in the documentation repository first when the workspace will
own cross-repository canonical documents such as the BRD. Imported repositories use
role `participant`; importing the authority repository again never downgrades its role.

Without `--yes`, a non-empty plan returns a confirmation-required result and changes
nothing. With `--yes`, every source is initialized using the selected documentation
root, then the workspace registry is atomically replaced. Repeating the same import is
idempotent and reports `unchanged`.

All sources in one invocation use the same documentation root. Run a separate import
for repositories that intentionally use another root; the registry preserves earlier
entries.

## Options

| Option | Required | Default | Effect |
| --- | --- | --- | --- |
| `--source <path>` | Yes | — | Selects existing repository paths; repeat or provide several values, up to 20. |
| `--root <path>` | Yes | — | Selects the repository-relative documentation root used to initialize every source. |
| `--workspace <path>` | No | Current directory | Selects the directory that owns `.cis/workspace.yml`. |
| `--dry-run` | No | `false` | Plans the complete batch and registry without writing. |
| `--yes` | No | `false` | Confirms the reviewed batch initialization and registry changes. |
| `--format <format>` | No | `human` | Selects `human`, `json`, or `agent` output. |

## Canonical registry

```yaml
schema_version: 1
repositories:
  - id: orders-api
    path: ../orders-api
    documentation_root: docs/cis
    role: participant
  - id: orders-web
    path: ../orders-web
    documentation_root: docs/cis
    role: participant
```

Repository IDs must be unique. Every entry must agree with the repository's own
`.cis/repository.yml`. Missing repositories, duplicate paths or identities, invalid
configuration, and mismatched documentation roots invalidate the workspace.

## Effects and safety

- Uses the same classification, curated starters, collision handling, and ownership
  rules as `cis repo init`.
- Plans the complete batch before changing the first repository.
- Never removes an earlier registry entry merely because it was omitted from a later
  import.
- Never builds graphs implicitly; use workspace graph build after import.
- Does not infer cross-repository graph edges.

If an initialization fails, run `cis repo doctor` for the affected source with the
same root, resolve its evidence-backed findings, and retry the complete import.

## Exit codes

| Code | Meaning |
| ---: | --- |
| `0` | Dry run, confirmed import, or unchanged reconciliation succeeded. |
| `2` | Workspace, source, root, configuration, or output format is invalid. |
| `3` | Review and confirmation are required before mutation. |
| `4` | A repository initialization or workspace identity collision exists. |

## Examples

```powershell
cis repo import --workspace C:\work\commerce --source C:\work\orders-api C:\work\orders-web --root docs/cis --dry-run --format agent
cis repo import --workspace C:\work\commerce --source C:\work\orders-api C:\work\orders-web --root docs/cis --yes
cis graph build --workspace C:\work\commerce --format agent
```

## Related commands

- [`cis repo list`](cis_repo_list.md)
- [`cis workspace init`](cis_workspace_init.md)
- [`cis repo init`](cis_repo_init.md)
- [`cis repo doctor`](cis_repo_doctor.md)
- [`cis graph build`](cis_graph_build.md)

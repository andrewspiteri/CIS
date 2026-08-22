---
title: "cis workspace init"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-20"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-workspace-init
---

# `cis workspace init`

Initializes a repository as the canonical documentation authority for a
multi-repository CIS workspace.

## Synopsis

```text
cis workspace init --root <repository-relative-path>
  [--repo <path>] [--dry-run] [--yes]
  [--format <human|json|agent>]
```

The command plans normal repository initialization and an authority entry in
`.cis/workspace.yml`. It preserves existing participant registrations, permits only one
authority, requires review before mutation, and is idempotent.

The authority receives a workspace-scoped technical-intent seed. Repositories later
registered as participants retain repository-scoped technical-intent seeds.

With `--format agent`, output includes the authority result plus the underlying
repository classification, component evidence, selected starters, planned create/update/
quarantine/retain routes, and explicit warning, collision, and error counts. Lists are
bounded and report omitted-item counts so an agent can review the plan without unbounded
output.

## Options

| Option | Required | Default | Effect |
| --- | --- | --- | --- |
| `--root <path>` | Yes | — | Selects the authority repository's documentation root. |
| `--repo <path>` | No | Current directory | Selects the documentation repository and workspace directory. |
| `--dry-run` | No | `false` | Plans repository and workspace changes without writing. |
| `--yes` | No | `false` | Confirms the reviewed authority initialization. |
| `--format <format>` | No | `human` | Selects human, JSON, or agent output. |

## Effects and safety

- Uses the same classification, starters, catalog, and collision rules as `repo init`.
- Registers the documentation repository with role `authority`.
- Rejects a second authority instead of silently replacing it.
- Does not import product repositories or build graphs.

## Exit codes

| Code | Meaning |
| ---: | --- |
| `0` | Dry run, confirmed initialization, or unchanged reconciliation succeeded. |
| `2` | Repository, root, registry, or format is invalid. |
| `3` | Human confirmation is required. |
| `4` | Repository content or workspace authority collides. |

## Example

```powershell
cis workspace init --repo C:\work\product-docs --root docs --dry-run --format agent
cis workspace init --repo C:\work\product-docs --root docs --yes
```

## Related commands

- [`cis repo import`](cis_repo_import.md)
- [`cis repo list`](cis_repo_list.md)
- [`cis graph build`](cis_graph_build.md)

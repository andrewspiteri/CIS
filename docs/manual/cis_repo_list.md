---
title: "cis repo list"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-05"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-repo-list
---

# `cis repo list`

Validates a CIS workspace registry and lists its initialized repositories.

## Synopsis

```text
cis repo list [--workspace <path>] [--format <human|json|agent>]
```

## Options

| Option | Required | Default | Effect |
| --- | --- | --- | --- |
| `--workspace <path>` | No | Current directory | Selects the directory containing `.cis/workspace.yml`. |
| `--format <format>` | No | `human` | Selects `human`, `json`, or `agent` output. |

## Validation and output

The command resolves relative registry paths, verifies each source directory and
`.cis/repository.yml`, checks repository identity and documentation-root agreement,
and rejects duplicate IDs or paths. It is read-only and does not initialize, repair,
or graph a repository.

Human output first identifies the ecosystem and governed product, then lists repository
ID, absolute resolved path, documentation root, structural role, owned/dependency
participation, producer/consumer direction, and component scope.
JSON emits the workspace resolution. Agent output emits one stable `repository=` line
per entry.

## Exit codes

| Code | Meaning |
| ---: | --- |
| `0` | The workspace registry and every repository entry are valid. |
| `2` | The workspace registry is missing or invalid, or a repository cannot resolve. |

## Example

```powershell
cis repo list --workspace C:\work\commerce --format agent
```

## Related commands

- [`cis repo import`](cis_repo_import.md)
- [`cis graph build`](cis_graph_build.md)
- [`cis graph validate`](cis_graph_validate.md)

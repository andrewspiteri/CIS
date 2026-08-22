---
title: "cis docs inventory"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-08"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-docs-inventory
---

# `cis docs inventory`

Inventories Markdown files beneath a repository's configured documentation root.

## Synopsis

```text
cis docs inventory [--repo <path>] [--format <human|json|agent>]
```

## Options

| Option | Required | Default | Effect |
| --- | --- | --- | --- |
| `--repo <path>` | No | Current directory | Selects the initialized target repository. |
| `--format <format>` | No | `human` | Selects `human`, `json`, or `agent` output. Values are case-insensitive. |
| `-?`, `-h`, `--help` | No | — | Shows command help and exits without inventorying documents. |

## Operation and effects

The command:

1. Reads `.cis/repository.yml` from the selected repository.
2. Resolves the documentation root recorded during `cis repo init`.
3. Reads `<root>/catalog.yml`.
4. Recursively discovers Markdown files beneath the documentation root without traversing reparse points.
5. Reports each document's stable ID, path, title, type, status, authority, metadata source, catalog state, and front-matter health.

Metadata precedence is catalog entry, then YAML front matter, then path-based inference. Type inference recognizes architecture decisions, architecture, specifications, references, changes, and a general-document fallback.

The command is read-only and does not update Markdown or the catalog.

## Output

Human output lists each path with its type, catalog state, and title. JSON emits the complete inventory result using camel-case property names. Agent output begins with a summary and emits one record per document:

```text
document=<id>;path=<path>;type=<type>;authority=<authority>;cataloged=<true|false>
```

Warnings and errors are emitted as `warning=` and `error=` records in agent format.

## Exit codes

| Code | Meaning |
| --- | --- |
| `0` | The inventory completed successfully, including inventories that contain warnings. |
| `2` | Repository context is invalid or the output format is unsupported. |
| `5` | The repository context is valid, but the catalog or documentation could not be inventoried. |

## Examples

```powershell
cis docs inventory
cis docs inventory --repo C:\work\orders
cis docs inventory --format json
cis docs inventory --format agent
```

## Related commands

- [`cis repo init`](cis_repo_init.md)
- [`cis docs validate`](cis_docs_validate.md)

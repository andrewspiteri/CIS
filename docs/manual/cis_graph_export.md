---
title: "cis graph export"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-16"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-graph-export
---

# `cis graph export`

Exports the validated SQLite graph projection to a portable representation. Exporting
never changes canonical graph inputs.

## Synopsis

```text
cis graph export [--type <markdown|json|jsonl|dot>]
  [--output <repository-relative-file>] [--force]
  [--repo <path>] [--format <human|json|agent>]
```

`--type` selects the artifact representation. `--format` controls only the command's
status output.

## Options

| Option | Required | Default | Effect |
| --- | --- | --- | --- |
| `--type <type>` | No | `markdown` | Selects `markdown`, `json`, `jsonl`, or Graphviz `dot`. |
| `--output <file>` | No | `.cis/local/graph/export.<extension>` | Selects a repository-relative output file. |
| `--force` | No | `false` | Replaces a differing custom output file. |
| `--repo <path>` | No | Current directory | Selects an initialized repository. |
| `--format <format>` | No | `human` | Selects command output as `human`, `json`, or `agent`. |

## Export types

| Type | Content |
| --- | --- |
| `markdown` | Build summary plus node and evidence-backed edge tables. |
| `json` | The complete portable `CisGraphDocument`; this replaces the former default `graph.json` build artifact. |
| `jsonl` | One metadata record followed by deterministic node and edge records. |
| `dot` | Graphviz directed graph with node and relationship labels. |

## Safety and idempotence

Default outputs under `.cis/local/graph/` are disposable and updated automatically.
A custom existing output with different content requires `--force`. Identical output
reports `unchanged` and is not rewritten.

Even with `--force`, export refuses to replace:

- `.cis/repository.yml`;
- graph generation files;
- any canonical/source input recorded in the graph manifest; or
- anything beneath `.git/`.

Output must remain inside the repository. Writes use an atomic temporary-file move.
Structurally invalid graphs are not exported. Warning-level and stale graphs may be
exported, but their diagnostics and freshness remain visible in command output and
the Markdown export header.

## Exit codes

| Code | Meaning |
| --- | --- |
| `0` | Export was written or already unchanged. |
| `2` | Repository context, export type, output path, or status format is invalid. |
| `4` | The graph is unavailable or a custom output collision requires `--force`. |
| `5` | Graph validation found structural errors. |

## Examples

```powershell
cis graph export
cis graph export --type jsonl
cis graph export --type dot --output .cis/local/exports/context.dot
cis graph export --output reports/context-graph.md --force --format agent
```

## Related commands

- [`cis graph build`](cis_graph_build.md)
- [`cis graph validate`](cis_graph_validate.md)
- [`cis graph trace`](cis_graph_trace.md)

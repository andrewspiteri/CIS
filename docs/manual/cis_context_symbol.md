---
title: "cis context symbol"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-context-symbol
---

# `cis context symbol`

Retrieves an exact symbol with its immediate source-file, component ownership,
dependency, implementation, and test relationships.

## Synopsis

```text
cis context symbol --id <id> [--limit <number>] [--include-proposed]
  [--repo <path>] [--format <human|json|agent>]
```

The ID must resolve to `symbol`. The command traverses only `contains`, `belongs-to`,
`owns`, `depends-on`, `implemented-by`, and `verified-by` edges at depth one. Use
`cis graph find --kind symbol --text <name>` to discover a symbol's exact identity.

## Options

| Option | Required | Default | Effect |
| --- | --- | --- | --- |
| `--id <id>` | Yes | — | Selects an exact symbol graph key or local ID. |
| `--limit <number>` | No | `100` | Limits nodes, including the symbol, to 1–1000. |
| `--include-proposed` | No | `false` | Includes proposed relationships. |
| `--repo <path>` | No | Current directory | Selects an initialized repository. |
| `--format <format>` | No | `human` | Selects `human`, `json`, or `agent`. |

## Exit codes

The command uses the same exit codes as [`cis graph related`](cis_graph_related.md).

## Related commands

- [`cis graph find`](cis_graph_find.md)
- [`cis graph related`](cis_graph_related.md)
- [`cis context tests-for`](cis_context_tests_for.md)

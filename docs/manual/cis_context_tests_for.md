---
title: "cis context tests-for"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-context-tests-for
---

# `cis context tests-for`

Retrieves tests connected to an exact target through `verified-by` evidence.

## Synopsis

```text
cis context tests-for --id <id> [--limit <number>] [--include-proposed]
  [--repo <path>] [--format <human|json|agent>]
```

The target may be a contract, component, symbol, workflow, or other graph node. The
command searches `verified-by` in both directions at depth one. Structural project
references and naming similarity never count as verification.

## Options

| Option | Required | Default | Effect |
| --- | --- | --- | --- |
| `--id <id>` | Yes | — | Selects an exact target graph key or local ID. |
| `--limit <number>` | No | `100` | Limits nodes, including the target, to 1–1000. |
| `--include-proposed` | No | `false` | Includes proposed verification relationships. |
| `--repo <path>` | No | Current directory | Selects an initialized repository. |
| `--format <format>` | No | `human` | Selects `human`, `json`, or `agent`. |

## Exit codes

The command uses the same exit codes as [`cis graph related`](cis_graph_related.md).

## Related commands

- [`cis context contract`](cis_context_contract.md)
- [`cis context symbol`](cis_context_symbol.md)
- [`cis graph related`](cis_graph_related.md)

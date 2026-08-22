---
title: "cis graph related"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-graph-related
---

# `cis graph related`

Traverses a deterministic, cycle-safe neighbourhood around one exact graph node.

## Synopsis

```text
cis graph related --id <id> [--kind <kind>] [--edge <type,type>]
  [--direction <in|out|both>] [--depth <number>] [--limit <number>]
  [--include-proposed] [--repo <path>] [--format <human|json|agent>]
```

## Options

| Option | Required | Default | Effect |
| --- | --- | --- | --- |
| `--id <id>` | Yes | — | Selects an exact full graph key or node-local ID. |
| `--kind <kind>` | No | None | Disambiguates a local ID by start-node kind. |
| `--edge <type,type>` | No | All types | Restricts traversal to comma-separated edge types. |
| `--direction <direction>` | No | `both` | Traverses `in`, `out`, or `both`. |
| `--depth <number>` | No | `1` | Limits traversal depth to 1–10. |
| `--limit <number>` | No | `100` | Limits nodes, including the start node, to 1–1000. |
| `--include-proposed` | No | `false` | Includes proposed edges; rejected edges remain excluded. |
| `--repo <path>` | No | Current directory | Selects an initialized repository. |
| `--format <format>` | No | `human` | Selects `human`, `json`, or `agent`. |

Default traversal accepts `declared`, `discovered`, and `confirmed` edges. Each
traversal reports depth, direction, type, state, confidence, endpoints, and evidence.
Node and edge order is deterministic. The result explicitly reports truncation and
graph freshness.

## Exit codes

| Code | Meaning |
| --- | --- |
| `0` | Traversal completed, including no-match or stale results. |
| `2` | Repository context, start identity, bounds, direction, or format is invalid. |
| `4` | The graph generation is unavailable or incompatible. |

## Examples

```powershell
cis graph related --id orders-api --kind component --depth 2
cis graph related --id orders-api --edge owns,depends-on --direction out
cis graph related --id orders-api --include-proposed --format json
```

## Related commands

- [`cis graph find`](cis_graph_find.md)
- [`cis graph trace`](cis_graph_trace.md)
- [`cis graph export`](cis_graph_export.md)
- [`cis context symbol`](cis_context_symbol.md)
- [`cis context tests-for`](cis_context_tests_for.md)

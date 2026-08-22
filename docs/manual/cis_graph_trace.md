---
title: "cis graph trace"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-graph-trace
---

# `cis graph trace`

Finds deterministic shortest paths between two exact nodes in an existing graph.

## Synopsis

```text
cis graph trace --from <id> --to <id>
  [--from-kind <kind>] [--to-kind <kind>] [--edge <type,type>]
  [--direction <in|out|both>] [--max-depth <number>]
  [--max-paths <number>] [--include-proposed]
  [--repo <path>] [--format <human|json|agent>]
```

## Options

| Option | Required | Default | Effect |
| --- | --- | --- | --- |
| `--from <id>` | Yes | — | Selects the source full graph key or node-local ID. |
| `--to <id>` | Yes | — | Selects the target full graph key or node-local ID. |
| `--from-kind <kind>` | No | None | Disambiguates the source local ID. |
| `--to-kind <kind>` | No | None | Disambiguates the target local ID. |
| `--edge <type,type>` | No | All types | Restricts discovery to comma-separated edge types. |
| `--direction <direction>` | No | `both` | Traverses `in`, `out`, or `both`. |
| `--max-depth <number>` | No | `5` | Limits paths to 1–10 edges. |
| `--max-paths <number>` | No | `10` | Returns at most 1–100 equal-length shortest paths. |
| `--include-proposed` | No | `false` | Includes proposed edges; rejected edges remain excluded. |
| `--repo <path>` | No | Current directory | Selects an initialized repository. |
| `--format <format>` | No | `human` | Selects `human`, `json`, or `agent`. |

## Path semantics

Discovery is breadth-first, deterministic, and cycle-safe. It returns only shortest
paths. When several equal-length paths exist, they are ordered by stable edge identity
and bounded by `--max-paths`. Every step reports traversal direction, edge type,
state, confidence, and its evidence observations.

Default traversal accepts `declared`, `discovered`, and `confirmed` relationships.
Proposed relationships require `--include-proposed`; rejected and possibly-stale
relationships are excluded.

Statuses distinguish `matched`, `no-path`, `no-match`, `invalid-query`, and
`graph-unavailable`. Freshness and truncation are reported independently.

## Exit codes

| Code | Meaning |
| --- | --- |
| `0` | Trace completed, including no-path, no-match, or stale results. |
| `2` | Repository context, endpoint identity, bounds, direction, or format is invalid. |
| `4` | The graph generation is unavailable or incompatible. |
| `5` | A loaded graph reports error-level diagnostics. |

## Examples

```powershell
cis graph trace --from orders-api --to nuget/fluentvalidation
cis graph trace --from <contract-key> --to <test-key> --edge verified-by
cis graph trace --from <node> --to <node> --max-depth 8 --format agent
```

## Related commands

- [`cis graph find`](cis_graph_find.md)
- [`cis graph related`](cis_graph_related.md)
- [`cis graph export`](cis_graph_export.md)

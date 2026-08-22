---
title: "cis context search"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-context-search
---

# `cis context search`

Searches the current local graph for engineering context without rebuilding or
changing canonical repository content. It is the context-module view over the graph
find contract and preserves graph identity, freshness, authority, and diagnostics.

## Synopsis

```text
cis context search [--id <id>] [--kind <kind>] [--subtype <subtype>]
  [--facet <facet>] [--text <text>] [--limit <number>]
  [--repo <path>] [--format <human|json|agent>]
```

At least one search filter is required. Multiple filters are combined. Text lookup is
bounded and case-insensitive over labels, identities, and scalar graph properties; it
is not vector or semantic search.

## Options

| Option | Required | Default | Effect |
| --- | --- | --- | --- |
| `--id <id>` | No | — | Matches an exact graph key or node-local ID. |
| `--kind <kind>` | No | — | Matches an exact registered node kind. |
| `--subtype <subtype>` | No | — | Matches an exact repository-specific subtype. |
| `--facet <facet>` | No | — | Requires a facet such as `contract`, `test`, or `dependency`. |
| `--text <text>` | No | — | Performs bounded text matching. |
| `--limit <number>` | No | `50` | Limits results to 1–1000 nodes. |
| `--repo <path>` | No | Current directory | Selects an initialized repository. |
| `--format <format>` | No | `human` | Selects `human`, `json`, or `agent`. |

## Exit codes

The command uses the same exit codes as [`cis graph find`](cis_graph_find.md).

## Related commands

- [`cis context pack`](cis_context_pack.md)
- [`cis graph find`](cis_graph_find.md)


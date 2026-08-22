---
title: "cis graph find"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-16"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-graph-find
---

# `cis graph find`

Finds nodes in an existing local context graph. The command is read-only and never
builds or changes the graph.

## Synopsis

```text
cis graph find [--id <id>] [--kind <kind>] [--subtype <subtype>]
  [--facet <facet>] [--text <text>] [--limit <number>]
  [--repo <path>] [--format <human|json|agent>]
```

At least one of `--id`, `--kind`, `--subtype`, `--facet`, or `--text` is required.
Filters are combined with AND.

## Options

| Option | Required | Default | Effect |
| --- | --- | --- | --- |
| `--id <id>` | Conditional | None | Matches an exact full graph key or node-local ID. |
| `--kind <kind>` | Conditional | None | Matches an exact node kind. |
| `--subtype <subtype>` | Conditional | None | Matches an exact node subtype. |
| `--facet <facet>` | Conditional | None | Requires an exact node facet. |
| `--text <text>` | Conditional | None | Case-insensitive match over label, local ID, and properties. |
| `--limit <number>` | No | `50` | Limits results to 1–1000 nodes. |
| `--repo <path>` | No | Current directory | Selects an initialized repository. |
| `--format <format>` | No | `human` | Selects `human`, `json`, or `agent`. |

Text lookup uses a contentless SQLite FTS5 trigram projection over labels, local IDs,
and properties. This preserves case-insensitive substring routing without loading or
scanning a repository-wide graph document. It remains bounded text matching, not
semantic or vector search. Results
are ordered by stable graph key and report whether the limit truncated the match set.

## Freshness and graph state

The command requires `.cis/local/graph/context.db`. It reads build, input, and
extractor metadata from SQLite and compares
extractor versions, input hashes, missing inputs, and newly discovered implementation
inputs. A stale graph remains queryable, but every output format reports `stale` and a
diagnostic instructing the caller to run `cis graph build`.

## Exit codes

| Code | Meaning |
| --- | --- |
| `0` | Query completed, including no-match or stale results. |
| `2` | Repository context, query options, or output format is invalid. |
| `4` | The graph generation is missing, unreadable, corrupt, or incompatible. |

## Examples

```powershell
cis graph find --kind reference-item --facet contract
cis graph find --kind symbol --text OrderEndpoint --format agent
cis graph find --id "api-operation/orders-api%3Aget%3Aorders-id"
```

## Related commands

- [`cis graph build`](cis_graph_build.md)
- [`cis graph related`](cis_graph_related.md)
- [`cis context contract`](cis_context_contract.md)

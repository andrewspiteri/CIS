---
title: "cis context references"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-context-references
---

# `cis context references`

Retrieves the immediate documents, governed reference items, and descriptive evidence
connected to one exact graph target.

## Synopsis

```text
cis context references --id <id> [--limit <number>] [--include-proposed]
  [--repo <path>] [--format <human|json|agent>]
```

The command traverses `contains`, `declares`, `references`, `governed-by`, and
`describes` relationships in both directions at depth one. Results preserve edge
state, confidence, observations, graph freshness, and truncation. A missing mapping
is reported as a gap; the command does not invent a relationship from name similarity.

## Options

| Option | Required | Default | Effect |
| --- | --- | --- | --- |
| `--id <id>` | Yes | — | Selects an exact full graph key or node-local ID. |
| `--limit <number>` | No | `100` | Limits nodes, including the target, to 1–1000. |
| `--include-proposed` | No | `false` | Includes proposed relationships; rejected edges remain excluded. |
| `--repo <path>` | No | Current directory | Selects an initialized repository. |
| `--format <format>` | No | `human` | Selects `human`, `json`, or `agent`. |

## Exit codes

The command uses the same exit codes as [`cis graph related`](cis_graph_related.md).

## Related commands

- [`cis context search`](cis_context_search.md)
- [`cis context pack`](cis_context_pack.md)


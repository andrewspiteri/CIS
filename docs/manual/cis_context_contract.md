---
title: "cis context contract"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-context-contract
---

# `cis context contract`

Retrieves an exact governed reference item with its immediate declaring document,
owner, implementation, consumer, producer, and verifying-test relationships.

## Synopsis

```text
cis context contract --id <id> [--limit <number>] [--include-proposed]
  [--repo <path>] [--format <human|json|agent>]
```

The ID must resolve to `reference-item`. The command traverses only `declares`,
`owns`, `implemented-by`, `consumed-by`, `produced-by`, and `verified-by` edges at
depth one. It does not infer contract relationships from text similarity.

## Options

| Option | Required | Default | Effect |
| --- | --- | --- | --- |
| `--id <id>` | Yes | — | Selects an exact contract graph key or local ID. |
| `--limit <number>` | No | `100` | Limits nodes, including the contract, to 1–1000. |
| `--include-proposed` | No | `false` | Includes proposed relationships. |
| `--repo <path>` | No | Current directory | Selects an initialized repository. |
| `--format <format>` | No | `human` | Selects `human`, `json`, or `agent`. |

Freshness, confidence, state, evidence locations, truncation, and partial-graph
diagnostics remain visible in every output format.

## Exit codes

The command uses the same exit codes as [`cis graph related`](cis_graph_related.md).

## Related commands

- [`cis graph find`](cis_graph_find.md)
- [`cis context tests-for`](cis_context_tests_for.md)

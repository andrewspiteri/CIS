---
title: "cis context callers"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-context-callers
---

# `cis context callers`

Retrieves incoming call, dependency, subscriber, and reference evidence for one exact
graph target.

## Synopsis

```text
cis context callers --id <id> [--limit <number>] [--include-proposed]
  [--repo <path>] [--format <human|json|agent>]
```

The command traverses incoming compiler-backed `calls` plus `depends-on`,
`subscribes-to`, and `references` relationships at depth one. Outgoing calls are
excluded. Use `cis graph related --direction both` to inspect both directions or
to inspect `consumed-by` relationships, which point from a contract to its consumer.
For C#, `calls` is produced by Roslyn semantic binding and carries high-confidence
invocation evidence. Other languages continue to expose structurally declared
consumers and dependencies until their compiler adapters provide equivalent binding.

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

- [`cis context contract`](cis_context_contract.md)
- [`cis graph trace`](cis_graph_trace.md)

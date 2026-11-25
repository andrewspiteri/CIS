---
title: "cis impact analyse"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-impact-analyse
---

# `cis impact analyse`

By default, discovers deterministic impact proposals through bounded graph traversal
and writes them to the change dossier. `--current` returns a current implementation
projection instead.

```text
cis impact analyse <change-id> [--root <node-id>[#<kind>]]...
  [--depth <1-10>] [--limit <1-1000>] [--include-proposed] [--current]
  [--repo <path>] [--format <human|json|agent>]
```

In the default mode, command roots override proposal roots. Stable finding IDs preserve existing human
dispositions across reruns. The command refuses a missing or ambiguous root, a changed
graph-build baseline, unavailable graph, or invalid bounds. Proposed graph edges are
excluded by default. When an explicit root is a catalogued feature specification, each
valid repository ID in its top-level `targets` front matter is also emitted as a
high-confidence proposed repository finding. This preserves cross-repository routing even
before the planned implementation exists and never accepts the targets automatically.
Without `--current`, exit `0` means analysis was recorded; exit `2` means invalid
or baseline-incompatible analysis.

`--current` checks or refreshes the current implementation graph and returns a
bounded projection instead of writing dossier findings. It preserves the original
proposal, graph baseline, approvals and finding dispositions; `applied` is `false`.
Command roots still override proposal roots. Current analysis accepts at most 20
roots and returns at most 1,000 distinct findings across them.

JSON identifies this mode with `analysisBasis: "current-implementation"`.
`current-analysed` with `decisionSuitability: "bounded-current"` reports a current
bounded selection. Extraction warnings, a partial graph or traversal/output limits
produce `current-partial` and `incomplete-current`; these findings do not establish
complete impact. A missing or untrustworthy graph, unresolved root or input change
during analysis fails rather than silently mixing generations. Exit `0` in this
mode means the projection was returned, including a disclosed partial projection;
it does not accept findings or authorize implementation.

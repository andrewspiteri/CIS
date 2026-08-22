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

Discovers deterministic impact proposals through bounded graph traversal and writes
them to the change dossier.

```text
cis impact analyse <change-id> [--root <node-id>[#<kind>]]...
  [--depth <1-10>] [--limit <1-1000>] [--include-proposed]
  [--repo <path>] [--format <human|json|agent>]
```

Command roots override proposal roots. Stable finding IDs preserve existing human
dispositions across reruns. The command refuses a missing or ambiguous root, a changed
graph-build baseline, unavailable graph, or invalid bounds. Proposed graph edges are
excluded by default. When an explicit root is a catalogued feature specification, each
valid repository ID in its top-level `targets` front matter is also emitted as a
high-confidence proposed repository finding. This preserves cross-repository routing even
before the planned implementation exists and never accepts the targets automatically.
Exit `0` means analysis was recorded; exit `2` means invalid or baseline-incompatible
analysis.

---
title: "cis standards applicable"
type: manual
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-15"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-standards-applicable
---

# `cis standards applicable`

Resolves active standards that govern affected delivery surfaces.

```text
cis standards applicable --target <target> [--target <target>...] [--stack <stack>...] [--repo <path>] [--format <human|json|agent>]
```

At least one `--target` is required. A standard matches when any requested target is declared and its stack is absent, `generic`, `all`, or one of the requested stacks. Only `Active` standards are returned.

The result is a routing aid: agents and humans must read the returned canonical files before implementation. An empty successful result does not prove that governance is complete. Exit code `2` is used for an invalid output format and `5` for a missing target or invalid repository state.

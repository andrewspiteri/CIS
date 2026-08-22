---
title: "cis diagnostics sources"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-14"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-diagnostics-sources
---

# `cis diagnostics sources`

List configured runtime evidence sources.

```text
cis diagnostics sources [--repo <path>] [--format <human|json|agent>]
```

Sources are canonical profile rows; sensitive sources are never read directly.

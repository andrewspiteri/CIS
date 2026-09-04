---
title: "cis references source build"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-30"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-references-source-build
---

# `cis references source build`

Rebuild source-document projections and anchor-level change evidence.

```text
cis references source build [--id <BRD-SRC-ID>] [--repo <path>] [--format <human|json|agent>]
```

Only derived files under `.cis/local/references/` change. The BRD and accepted digest remain untouched. `diff.json` records added, changed, removed, and cited anchors and whether the change is material to the BRD.

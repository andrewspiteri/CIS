---
title: "cis references source status"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-30"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-references-source-status
---

# `cis references source status`

Compare registered and detected source digests without modifying files.

```text
cis references source status [--id <BRD-SRC-ID>] [--repo <path>] [--format <human|json|agent>]
```

The command reports source drift and whether the latest projection proves that a cited BRD anchor changed or disappeared. A stale projection is reported with a build instruction.

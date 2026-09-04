---
title: "cis ci artifacts"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-ci-artifacts
---

# `cis ci artifacts`

List artifact metadata for one workflow run without downloading artifact contents.

```text
cis ci artifacts --run <id> [--provider <kind>] [--repository <owner/repository>]
  [--repo <path>] [--format <human|json|agent>]
```

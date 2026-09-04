---
title: "cis ci rerun-failed"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-ci-rerun-failed
---

# `cis ci rerun-failed`

Request a remote rerun of failed jobs.

```text
cis ci rerun-failed --run <id> --yes [--provider <kind>]
  [--repository <owner/repository>] [--repo <path>] [--format <human|json|agent>]
```

This is a remote mutation. Without explicit `--yes`, the command returns an invalid-request result and performs no action.

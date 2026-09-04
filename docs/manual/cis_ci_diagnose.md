---
title: "cis ci diagnose"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-ci-diagnose
---

# `cis ci diagnose`

Classify failed jobs and preserve bounded redacted evidence.

```text
cis ci diagnose --run <id> [--provider <kind>] [--repository <owner/repository>]
  [--repo <path>] [--format <human|json|agent>]
```

At most five failed, timed-out, or cancelled job logs are retrieved per invocation.

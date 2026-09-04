---
title: "cis ci logs"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-ci-logs
---

# `cis ci logs`

Download one job log as bounded redacted local evidence.

```text
cis ci logs --job <id> [--provider <kind>] [--repository <owner/repository>]
  [--repo <path>] [--format <human|json|agent>]
```

The received bytes are hashed; retained text is redacted, capped, and stored beneath `.cis/local/ci/`.

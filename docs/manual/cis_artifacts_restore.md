---
title: "cis artifacts restore"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-artifacts-restore
---

# `cis artifacts restore`

Verify and restore an archive to its original local paths.

```text
cis artifacts restore --archive <id> --yes [--repo <path>] [--format <human|json|agent>]
```

Different existing content is a conflict and is never overwritten. Missing confirmation, digest mismatch, or a conflict returns exit code `4`.

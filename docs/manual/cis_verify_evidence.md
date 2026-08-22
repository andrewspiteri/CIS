---
title: "cis verify evidence"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-14"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-verify-evidence
---

# `cis verify evidence`

Append one exact evidence row to canonical verification Markdown.

```text
cis verify evidence <change-id> --task <id> --check <name> --artifact <command-or-path> --result <result> [--notes <text>] [--repo <path>]
```

Record exact commands and artifacts; do not summarize a check that was not run.

---
title: "cis artifacts retrieve"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-artifacts-retrieve
---

# `cis artifacts retrieve`

Verify and non-destructively extract an archive or one retained entry for inspection.

```text
cis artifacts retrieve --archive <id> [--entry <original-path>] [--repo <path>] [--format <human|json|agent>]
```

Output remains below `.cis/local/artifacts/retrieved/`. Digest mismatch, missing entries, and traversal fail with exit code `4`.

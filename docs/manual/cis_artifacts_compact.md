---
title: "cis artifacts compact"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-artifacts-compact
---

# `cis artifacts compact`

Create and verify a reversible archive before removing eligible original entries.

```text
cis artifacts compact --family <archive-family> --yes [--repo <path>] [--format <human|json|agent>]
```

The command rejects non-archive families and missing confirmation. It records a content-hashed ZIP and manifest beneath `.cis/local/artifacts/archive/`.

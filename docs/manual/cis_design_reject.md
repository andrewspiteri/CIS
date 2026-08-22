---
title: "cis design reject"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-13"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-design-reject
---

# `cis design reject`

Rejects a `PausedForReview` design while preserving evidence and the global pause.

```text
cis design reject <change-id> --reviewer <identity> --reason <rationale> [--repo <path>] [--format <human|json|agent>]
```

CIS preserves renderer and PNG hashes plus rejection rationale, then removes rejected
PNG files. Only wireframe/design revision is permitted until a replacement pack is
rendered and approved.


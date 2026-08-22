---
title: "cis tracker pull"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-14"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-tracker-pull
---

# `cis tracker pull`

Reads external issue mirrors and persists remote-only, concurrent, or deletion drift
as conflicts. It never rewrites canonical task fields, lifecycle, evidence, approvals,
deferrals, or acceptance.

```text
cis tracker pull <change-id> [--provider <key>] [--repo <path>]
  [--format <human|json|agent>]
```

Conflicts live in `.cis/local/trackers/conflicts.json`, are reported by repository
doctor, and require `cis tracker resolve` with explicit human authority.


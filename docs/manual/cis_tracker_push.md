---
title: "cis tracker push"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-14"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-tracker-push
---

# `cis tracker push`

Creates missing external issue mirrors and updates remote-only projections when the
canonical task changed. It never overwrites an open conflict, recreates a deleted
remote item automatically, or grants the tracker CIS approval/completion authority.

```text
cis tracker push <change-id> [--provider <key>] [--repo <path>]
  [--format <human|json|agent>]
```

Successful writes update the task's durable `External issue links` row and disposable
`.cis/local/trackers/state.json`. Review `cis tracker plan`, repository delivery policy,
the exact target, and credential authority before invoking this remote mutation.


---
title: "cis agent show"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-03"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-agent-show
---

# `cis agent show`

Show one run and its retained provenance.

```text
cis agent show <run-id> [--summary] [--repo <path>] [--format <human|json|agent>]
```

The result includes the current manifest, append-only normalized events, permission decisions, structured result, and content-hashed artifact inventory. It does not read provider credential stores or mutate the run.

`--summary` omits raw append-only events from JSON while retaining the manifest, structured
result, permissions, artifacts, and distinct event kinds. Use it for workspace-state
projection; omit it when a human explicitly opens the complete run evidence.

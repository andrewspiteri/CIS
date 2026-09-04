---
title: "cis generate applicable"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on command or applicability rule change"
cis:
  stable_id: change-impact-studio:manual:cis-generate-applicable
---

# `cis generate applicable`

Rank deterministic templates for a task without a model call.

```text
cis generate applicable --task <description> [--changed-file <relative-path> ...]
  [--repo <path>] [--format <human|json|agent>]
```

The result includes confidence, deterministic reasons, and required model fields. Safe
repository-relative changed paths are required.

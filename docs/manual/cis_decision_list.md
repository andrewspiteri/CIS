---
title: "cis decision list"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-decision-list
---

# `cis decision list`

Lists change-local decisions with category, options, evidence, blocking state,
required-before gate, resolution, rationale, and ADR promotion.

```text
cis decision list <change-id> [--status <open|resolved|deferred>]
  [--repo <path>] [--format <human|json|agent>]
```

The command is read-only. Invalid or duplicate managed decision rows return exit `2`.

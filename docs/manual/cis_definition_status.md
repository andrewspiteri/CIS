---
title: "cis definition status"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-04"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-definition-status
---

# `cis definition status`

```text
cis definition status [--workspace <path>] [--format <human|json|agent>]
```

Reports all eight pages, current/completion state, canonical artifact paths, issues, applicable
dictionaries, generated diagrams, UI-preview metadata, session identity, and consolidated
activation readiness. It is read-only and does not weaken the ordinary Active-document gates.

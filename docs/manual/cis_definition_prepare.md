---
title: "cis definition prepare"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-04"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-definition-prepare
---

# `cis definition prepare`

```text
cis definition prepare --page <foundation|business|technical|architecture|contracts|experience|delivery|review> [--workspace <path>] [--format <human|json|agent>]
```

Prepares or refreshes one page from the exact current upstream draft. Technical and experience
pages initialize their questionnaires and generate their governed documents after every decision
is resolved. Architecture generates the solution-design bundle and high-level diagrams. Delivery
builds the high-level backlog. The command never records human approval.

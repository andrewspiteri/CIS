---
title: "cis brd backlog approve"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-16"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-brd-backlog-approve
---

# `cis brd backlog approve`

```text
cis brd backlog approve --reviewer <human> --reason <rationale>
  [--workspace <path>] [--format <human|json|agent>]
```

Records explicit human approval of a valid/current high-level backlog, including the
reviewer, UTC timestamp, rationale, and normalized approved-content digest. This
authorizes feature-specification preparation only. Agents must not invoke it without
explicit authority for the reviewer and rationale.


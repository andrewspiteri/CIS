---
title: "cis agent resume"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-28"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-agent-resume
---

# `cis agent resume`

Create a new retained attempt using a provider session from a terminal run.

```text
cis agent resume <run-id> --actor <identity> --reason <rationale> [--message <continuation>] [--approve-requests] [--repo <path>] [--format <human|json|agent>]
```

The provider must support resumption, the original worktree and unchanged envelope must remain available, and the prior run must be terminal. CIS sends the complete digest-bound task contract and artifact route on every attempt, followed by the optional continuation, so a failed pre-session attempt cannot resume with an unbounded or context-free prompt. The next attempt appends provenance and never erases the first result. Ctrl+C follows the same bounded cancellation behavior as `cis agent run`.

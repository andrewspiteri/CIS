---
title: "cis plan show"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-plan-show
---

# `cis plan show`

Displays the canonical plan and all bounded work records.

```text
cis plan show <change-id> [--repo <path>] [--format <human|json|agent>]
```

The command is read-only and reports objectives, complexity, parent/child decomposition,
feature-requirement and impact coverage, dependencies, acceptance criteria, validation,
task status, and imported feature-specification provenance.

For imported feature plans, every row includes its durable `agent-tasks/WORK-NNN.md`
path. Open that document for full scope, exclusions, evidence, gates, acceptance
checklists, targeted validation, completion evidence, and deferral state.

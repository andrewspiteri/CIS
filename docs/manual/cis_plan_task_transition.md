---
title: "cis plan task transition"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-22"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-plan-task-transition
---

# `cis plan task transition`

Moves one generated task through its governed lifecycle.

```text
cis plan task transition <change-id> <task-id> --status <status> --actor <identity> --reason <rationale> [--repo <path>] [--format <human|json|agent>]
```

Supported states are Draft, ReadyForReview, Approved, InProgress, Blocked, Deferred,
Complete, Cancelled, and Decomposed. Invalid transitions fail. Completion requires
resolved checklists and evidence. For UI-bearing plans, all downstream work is blocked
until `design.md` has `gate_status: Approved`. Every transition appends an audit event.
Completion also snapshots sanitized CLI-usage counts, failures, possible token savings,
and a ledger digest into the task and `verification.md` when local usage exists.

For imported plans, every transition first verifies that the canonical feature file
still matches the digest recorded in `plan.md`. Source drift blocks all task movement;
rerun same-path `cis plan import-spec` to generate a revised Draft before continuing.

When completing `core.delivery.final-sweep`, terminal disposition is role-aware:
executable tasks must be Complete/Deferred/Cancelled, approved wireframe and design
review gates remain terminal at Approved, and the coordination parent remains terminal
at Decomposed until the final sweep is complete. This preserves truthful review and
decomposition lifecycles while still rejecting unresolved executable work.

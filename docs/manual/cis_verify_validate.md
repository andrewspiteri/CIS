---
title: "cis verify validate"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-14"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-verify-validate
---

# `cis verify validate`

Validate the current workspace snapshot plus plan, design, task, and verification evidence gates.

```text
cis verify validate <change-id> [--repo <path>] [--format <human|json|agent>]
```

Errors block final verification acceptance. Validation rejects legacy schema, empty or digest-invalid snapshots, and any repository change made after `cis verify diff`.

The completion gate treats lifecycle according to task role:

- implementation, verification, assurance, and other executable tasks must be
  `Complete`, `Completed`, or `Accepted`;
- wireframe and visual-design review tasks are terminal at `Approved`;
- the coordination parent is terminal at `Decomposed` after its child graph is created;
- the active `core.delivery.final-sweep` task may validate while `InProgress` only when
  its checklists are resolved and it already records passing completion evidence.

These role-specific dispositions avoid converting approved review gates into execution
tasks and avoid requiring the final sweep to complete before it can run its own gate.

---
title: "cis verify validate"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-23"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-verify-validate
---

# `cis verify validate`

Validate the current workspace snapshot plus plan, design, task, and verification evidence gates.

```text
cis verify validate <change-id> [--repo <path>] [--format <human|json|agent>]
```

Errors block final verification acceptance. Validation rejects legacy schema, empty or digest-invalid snapshots, any repository change made after `cis verify diff`, a changed imported feature specification, or a stale governed feature approval. A changed feature must be renewed and re-imported so the delivery plan returns to Draft for explicit review.

For an imported feature plan, validation also requires every generated `TC-*` manual
case to be present in a recognized automated test source across the registered
workspace. The exact ID may be in a test name, framework metadata, or adjacent
traceability annotation. The derived catalogue must show `Automated` and the current
`repository::path:line` reference; otherwise validation returns
`CIS-VERIFY-AUTOMATION-COVERAGE`. Run the same `cis plan import-spec` after test changes
to refresh these mappings before recapturing the verification snapshot.

Git subprocess output is drained concurrently and each Git operation is bounded to 30 seconds. A timeout becomes a `CIS-VERIFY-GIT-TIMEOUT` finding instead of leaving the workflow waiting indefinitely.

The completion gate treats lifecycle according to task role:

- implementation, verification, assurance, and other executable tasks must be
  `Complete`, `Completed`, or `Accepted`;
- wireframe and visual-design review tasks are terminal at `Approved`;
- the coordination parent is terminal at `Decomposed` after its child graph is created;
- the active `core.delivery.final-sweep` task may validate while `InProgress` only when
  its checklists are resolved and it already records passing completion evidence.

These role-specific dispositions avoid converting approved review gates into execution
tasks and avoid requiring the final sweep to complete before it can run its own gate.

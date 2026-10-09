---
title: "cis plan performance-check"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-10-06"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-plan-performance-check
---

# `cis plan performance-check`

Check a native BenchmarkDotNet full JSON report against the adopted performance baseline and its current workflow execution. This read-only command does not select a baseline, change budgets or complete a task.

```text
cis plan performance-check --candidate <repository-relative-report> --workflow <repository-relative-state.json> [--repo <path>] [--format human|json|agent]
```

The repository must adopt engineering completion and contain a reviewed `.cis/performance-policy.json`. Follow the policy contract in [completion context](cis_plan_task_completion_context.md#performance-baseline). The workflow must contain a successful `performance` step and retain the current execution-input identity. Missing reports, incompatible host/runtime/workload configuration, incomplete samples, excessive noise, stale execution or exceeded budgets fail the check.

Human output states the outcome; JSON includes schema version, status, errors and exit code; agent output provides status and exit code. Diagnostics are also written to stderr. Exit 0 means the selected evidence meets this scoped policy. Exit 4 means unavailable, invalid or failing evidence; invalid format returns 2. A passing result does not establish that the selected scope covers every product workload or that the resource conditions were controlled: the assigned reviewer checks those claims.

---
title: "cis workflow run"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-workflow-run
---

# `cis workflow run`

Start or resume a checkpointed workflow run.

```text
cis workflow run <workflow> [--run-id <id>] [--repo <path>] [--format <human|json|agent>]
```

Commands execute without a shell. Successful steps are skipped on resume; definition drift requires a new run.

Each attempt streams timestamped standard output and standard error into
`.cis/local/workflows/<run-id>/<step>.log`; later attempts use
`<step>.attempt-<number>.log`. CIS redacts common credential values before persistence,
flushes output while the process is running, and preserves partial output on timeout.
Each log is bounded below 10 MiB and records truncation in workflow state. Diagnostic
reruns never replace an earlier attempt log.
Child processes receive `CIS_WORKFLOW_RUN_ID`, `CIS_WORKFLOW_STEP_ID`, and
`CIS_WORKFLOW_ATTEMPT` so suite harnesses can correlate derived diagnostics.

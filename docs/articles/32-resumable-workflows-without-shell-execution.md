---
title: "Resumable Workflows Without Shell Execution"
type: article
status: Draft
series: "Human and Agent Execution"
series_order: 7
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: on workflow-engine change
summary: "Ordered executable-and-argument steps, dependency checks, timeouts, checkpoints, and digest-safe resume without evaluating shell text."
cis:
  stable_id: change-impact-studio:article:resumable-workflows-without-shell
---

# Resumable workflows without shell execution

Automation definitions often store arbitrary shell strings. They are flexible, but hard
to validate, quote safely, resume reliably, or expose to an agent without broad execution
authority.

CIS workflows use structured steps.

## A workflow step is data

Each step declares:

- stable step ID;
- executable and separate argument list;
- dependencies;
- continue-on-failure choice; and
- timeout.

CIS launches the executable directly. It does not ask a shell to interpret pipes,
substitutions, redirects, or chained commands hidden in the definition.

## Dependencies are validated first

Missing step references and cycles fail before execution. A downstream step runs only
when its declared prerequisites reach the required state.

## Checkpoint every result

The runner records bounded output, timing, exit code, and status after each step. A
stopped workflow can resume successful steps only when the workflow definition digest
is unchanged.

If the definition changed, earlier results are evidence for another workflow and cannot
be reused silently.

## Logs are evidence, not approval

Workflow summaries help verification, but a zero exit code cannot grant task completion,
plan approval, or acceptance. The canonical task defines which workflow evidence is
required.

## Takeaway

Represent automation as validated executable-and-argument steps, not opaque shell text.
Checkpoint each step, bind resume to the definition digest, bound outputs and timeouts,
and keep workflow success separate from governed acceptance.

## Canonical CIS sources

- [Execution, assurance, diagnostics, and learning](../specs/execution-assurance-and-learning-spec.md)
- [`cis workflow run`](../manual/cis_workflow_run.md)
- [Standard delivery workflow](../workflows/standard-delivery.md)


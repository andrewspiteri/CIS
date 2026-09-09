---
title: "Resumable Workflows Without Shell Execution"
type: article
status: Draft
series: "Human and Agent Execution"
series_order: 7
owner: "Andrew Spiteri"
last_reviewed: "2026-09-09"
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

Older local runs can be inventoried, archived, compacted, restored, or retrieved under the
local-artifact policy. Archive integrity and recovery preserve useful diagnostics without
turning derived workflow state into canonical completion evidence.

## Logs are evidence, not approval

Workflow summaries help verification, but a zero exit code cannot grant task completion,
plan approval, or acceptance. The canonical task defines which workflow evidence is
required.

## Structured execution narrows the trust boundary

A shell command is a small program. It can expand variables, evaluate substitutions,
redirect files, invoke nested shells, and change meaning across platforms. Storing that
program in a workflow makes static validation and containment difficult.

An executable plus an argument array has a smaller contract. CIS can show exactly which
process will start, validate every argument as data, apply a timeout, capture output, and
avoid shell-specific quoting. This does not make the executable safe by itself, but it
removes an unnecessary interpreter from the path.

## Model dependencies explicitly

Consider a delivery workflow with restore, build, focused tests, documentation validation,
packaging, and package smoke testing. Packaging depends on build; smoke testing depends on
the package; documentation validation may run independently after restore.

A dependency graph exposes that order before execution. It can reject missing IDs and
cycles, show which steps are blocked after failure, and allow independent diagnostics to
continue when policy explicitly permits. A chained shell string hides those semantics in
operators and exit behavior.

## Checkpoint enough to resume safely

Each checkpoint should bind the workflow digest, step ID, executable and arguments,
start and finish time, exit state, bounded output or artifact references, and dependency
outcomes. On resume, CIS reuses a successful step only when its definition and controlling
workflow identity are unchanged.

Changing a test argument, timeout, dependency, or continue-on-failure rule creates a new
execution contract. Reusing the old result would claim evidence for a step that never ran.

## Bound output without losing failure meaning

Long-running builds can generate enormous logs. CIS may retain bounded output and local
artifacts while preserving truncation, exit code, timing, and retrieval information. A
summary should never imply that omitted output was inspected.

When a structured result is required, process exit zero without that parseable artifact
is invalid evidence. Workflow state distinguishes successful process execution from a
valid engineering result.

## Cancellation and timeout are outcomes

A cancelled, startup-expired, idle-expired, or total-timeout step should retain its own
classification. These states guide recovery differently: cancellation may be intentional,
idle expiry may indicate a stalled process, and total expiry may require a larger bounded
timeout or smaller task.

Resume must not silently rerun irreversible steps. Workflow authors should keep mutation
explicit and idempotent or require a human recovery decision where replay could be unsafe.

## Resumability does not grant approval

Checkpointing improves reliability; it does not change authority. A resumed workflow can
produce build and test evidence for a task, but plan approval, exception disposition, and
final acceptance remain canonical human-controlled states. The workflow definition itself
must also be current and reviewed.

## Takeaway

Represent automation as validated executable-and-argument steps, not opaque shell text.
Checkpoint each step, bind resume to the definition digest, bound outputs and timeouts,
and keep workflow success separate from governed acceptance.

## Canonical CIS sources

- [Execution, assurance, diagnostics, and learning](../specs/execution-assurance-and-learning-spec.md)
- [`cis workflow run`](../manual/cis_workflow_run.md)
- [Standard delivery workflow](../workflows/standard-delivery.md)
- [Local artifact retention and recovery](../specs/local-artifact-retention-spec.md)

---
title: "Handing One Task to Different Coding Agents"
type: article
status: Draft
series: "CIS in Practice"
series_order: 8
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: on agent-provider change
summary: "A comparison method that keeps task authority constant while execution providers vary."
cis:
  stable_id: change-impact-studio:article:one-task-different-agents
---

# Handing one task to different coding agents

Comparing agents is difficult when each receives a different prompt or repository state.
CIS makes the bounded task the controlled variable.

## Prepare one approved task

The task contains exact objective, source digests, context, targets, non-goals, validation,
and result schema. Provider-specific envelopes are derived from the same identity.

## Isolate execution

Each provider works from the same baseline in a separate worktree or clean checkout.
Unavailable tools and checks are reported rather than silently substituted.

## Compare evidence, not prose

Useful comparison dimensions include scope adherence, unexpected files, missing planned
work, validation outcomes, retries, context expansion, elapsed time, and residual risk.

## Keep acceptance independent

The same verification process observes each Git diff. A more confident summary does not
outweigh stronger evidence from another result.

## Learn about the task too

If all providers misunderstand the same boundary, the task or repository knowledge is
likely incomplete. Provider comparison can improve planning rather than merely rank models.

## Takeaway

Compare executors against one canonical work contract and one evidence model. Vary the
provider, not the authority or definition of success.

## Canonical CIS sources

- [Task-type contract](../specs/task-type-contract-spec.md)
- [`cis agent prepare`](../manual/cis_agent_prepare.md)
- [Execution, assurance, diagnostics, and learning](../specs/execution-assurance-and-learning-spec.md)


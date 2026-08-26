---
title: "Human and Agent Execution from the Same Contract"
type: article
status: Draft
series: "Human and Agent Execution"
series_order: 3
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: on task-contract change
summary: "A bounded work item should be equally understandable to a developer and a coding agent."
cis:
  stable_id: change-impact-studio:article:human-agent-same-contract
---

# Human and agent execution from the same contract

Teams often maintain two planning systems: tickets for humans and elaborate prompts for
agents. The two drift. A constraint added to the ticket never reaches the prompt, or an
agent discovers a decision that never returns to project state.

One bounded contract can serve both.

## Write for an executor, not a model

A good work item explains the outcome, authority, evidence, dependencies, non-goals,
validation, and escalation route in plain engineering language. It does not depend on
model-specific persuasion or hidden conversation context.

A human may use the document as an implementation checklist. An agent envelope can
project the same fields into its execution environment.

## Keep the source canonical

Provider-specific envelopes are derived from the task path and digest. The repository
task remains canonical. If it changes, old envelopes become stale rather than competing
with the new instruction.

## Report through the same evidence model

Both human and agent executors should record exact changed paths, commands, results,
blockers, deferrals, and residual risks. Neither executor marks itself accepted.

## Improve the contract when execution struggles

If humans repeatedly need oral context or agents repeatedly broaden searches, the task
or repository knowledge is incomplete. Capture that as a learning opportunity instead
of adding provider-specific prompt folklore.

## Takeaway

Design work so any capable executor can understand the same reviewed contract. Derive
provider packaging from it, preserve one source of authority, and evaluate every result
through the same evidence and acceptance model.

## Canonical CIS sources

- [Task-type contract](../specs/task-type-contract-spec.md)
- [Change impact and bounded planning](../specs/change-impact-and-planning-spec.md)
- [`cis agent prepare`](../manual/cis_agent_prepare.md)


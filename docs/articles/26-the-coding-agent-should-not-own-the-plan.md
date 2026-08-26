---
title: "The Coding Agent Should Not Own the Plan"
type: article
status: Draft
series: "Human and Agent Execution"
series_order: 1
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: on agent-execution change
summary: "Why the executor should consume reviewed work rather than discover, approve, implement, and close its own scope."
cis:
  stable_id: change-impact-studio:article:agent-should-not-own-plan
---

# The coding agent should not own the plan

A coding agent can inspect a repository, propose a design, implement code, run tests,
and summarize the result. Asking it to do all of those in one instruction appears
efficient. It also gives one executor control over every definition of success.

## Planning and execution have different authority

Planning determines what belongs in scope, which decisions are required, how work is
decomposed, and what evidence will earn acceptance. Execution changes the repository
within those boundaries.

Combining them lets the agent silently resolve ambiguity in favor of whatever it can
implement most easily.

## Agents are excellent proposal engines

An agent can help identify impact, alternatives, missing acceptance criteria, task
boundaries, and validation commands. Those are valuable proposals. Human authorities
confirm meaning and material choices before the proposal becomes executable work.

## The plan must survive the provider

A plan that only exists in one conversation cannot be reviewed reliably or handed to a
different executor. CIS stores bounded work in repository-owned task documents with
stable IDs, source digests, dependencies, non-goals, and validation.

The executor can be changed without changing the approved task.

## Scope discovery continues during execution

Execution may reveal missing impact. The agent should preserve evidence and propose an
expansion, not absorb it silently. Review returns to the impact and plan authority.

## Completion remains independent

The agent's success report becomes evidence. Git comparison, required validation, and
human acceptance determine completion.

## Takeaway

Use coding agents to propose and execute, not to own the plan they are judged against.
Keep scope and acceptance in durable reviewed records that survive any executor.

## Canonical CIS sources

- [Product intent](../specs/product-intent-spec.md)
- [Task-type contract](../specs/task-type-contract-spec.md)
- [Execution, assurance, diagnostics, and learning](../specs/execution-assurance-and-learning-spec.md)


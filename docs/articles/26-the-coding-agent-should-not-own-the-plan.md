---
title: "The Coding Agent Should Not Own the Plan"
type: article
status: Draft
series: "Human and Agent Execution"
series_order: 1
owner: "Andrew Spiteri"
last_reviewed: "2026-09-09"
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

CIS can now coordinate a foreground provider run from that task. The controller selects
the provider, transport, target repository, run mode, and permission ceiling. The provider
receives the digest-bound contract; it does not acquire authority over the plan because it
can stream events, request a permitted capability, or produce a structured result.

## Scope discovery continues during execution

Execution may reveal missing impact. The agent should preserve evidence and propose an
expansion, not absorb it silently. Review returns to the impact and plan authority.

## Completion remains independent

The agent's success report becomes evidence. Git comparison, required validation, and
human acceptance determine completion.

## The conflict is structural, not personal

The problem is not that an agent lacks intelligence or good intent. Any executor has an
incentive to interpret ambiguity in a way that allows progress. A human developer may do
the same thing under schedule pressure. When the executor also defines the scope and
selects the evidence, that interpretation becomes difficult to challenge.

Separating planning from execution creates an independent reference point. The executor
can disagree with it, discover omissions, and recommend changes, but cannot silently
rewrite the test by which its own work will be assessed.

## Planning supplies a controlled question

Compare two instructions:

> Add invitation support and make sure it works.

and:

> Implement `WORK-005` against the approved invitation contract. Change the owned API
> repository only; preserve account-linking and authentication protocol behavior; run the
> listed authorization, expiry, and contract checks; stop and report if a consumer or
> migration outside accepted impact is discovered.

The second instruction does not tell the agent how to write every line. It establishes
the outcome, authority, non-goals, evidence, and escalation rule. That is the space in
which useful autonomy can operate.

## Agents can improve a plan without owning it

Before approval, an agent can inspect the proposed work and identify missing contracts,
weak acceptance criteria, unsafe dependencies, or validation that cannot establish the
claim. During execution, it can report that the task conflicts with source evidence. After
verification, it can help explain a failure or formulate a learning proposal.

In each case the output is a proposal with provenance. The relevant human authority
decides whether to change impact, resolve a decision, revise the task, or accept risk.
This separation uses the agent's analytical strength without confusing analysis with
approval.

## Direct execution needs a controller boundary

`cis agent run` does more than launch a provider. The controller checks task eligibility,
current digests, target ownership, mode, permission ceiling, transport capability,
timeouts, and worktree isolation. Provider requests are constrained by the declared run;
network or broader writes do not become allowed because the provider asks persuasively.

The controller also owns cancellation, event normalization, attempt history, and result
validation. Those controls make execution observable. They do not make the provider's
terminal success message authoritative.

## New evidence returns to governance

Suppose the agent discovers that invitation expiry also changes a public cached response.
Continuing may be technically possible, but the new surface introduces cache,
compatibility, and security obligations absent from the task. The correct response is to
preserve the evidence, stop the affected work, and propose scope expansion.

The plan may be revised and a new envelope prepared. That is not a failure of autonomy;
it is autonomy respecting the boundary where new product meaning entered the work.

## Keep acceptance independent of execution style

A human-written patch and an agent-written patch should face the same actual Git
comparison, contract checks, tests, missing and unexpected path review, and residual-risk
decision. Provenance may affect the depth of assurance, but neither authorship grants
completion.

## Takeaway

Use coding agents to propose and execute, not to own the plan they are judged against.
Keep scope and acceptance in durable reviewed records that survive any executor.

## Canonical CIS sources

- [Product intent](../specs/product-intent-spec.md)
- [Task-type contract](../specs/task-type-contract-spec.md)
- [Execution, assurance, diagnostics, and learning](../specs/execution-assurance-and-learning-spec.md)
- [Provider-neutral agent execution](../specs/features/agent-execution-coordination-feature.md)

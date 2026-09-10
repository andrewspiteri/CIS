---
title: "Human and Agent Execution from the Same Contract"
type: article
status: Active
series: "Human and Agent Execution"
series_order: 3
owner: "Andrew Spiteri"
last_reviewed: "2026-09-10"
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

For direct execution, CIS adds controller-owned run settings around that same contract:
provider, transport, mode, permission ceiling, target repository, isolation, and timeout.
Those settings constrain execution; they do not fork the engineering objective into a
provider-specific plan.

## Keep the source canonical

Provider-specific envelopes are derived from the task path and digest. The repository
task remains canonical. If it changes, old envelopes become stale rather than competing
with the new instruction.

## Report through the same evidence model

Both human and agent executors should record exact changed paths, commands, results,
blockers, deferrals, and residual risks. Neither executor marks itself accepted.

An agent's normalized event stream and terminal result remain derived run state until an
explicit import validates identity, freshness, containment, artifacts, and evidence. A
human result should face the same planned-versus-actual verification rather than gaining
trust merely because it was reported conversationally.

## Improve the contract when execution struggles

If humans repeatedly need oral context or agents repeatedly broaden searches, the task
or repository knowledge is incomplete. Capture that as a learning opportunity instead
of adding provider-specific prompt folklore.

## The contract defines obligations, not technique

A common task should be precise about what must become true and deliberately flexible
about reversible implementation choices. It identifies controlling sources, repository
targets, prohibited scope, acceptance criteria, validation, and escalation. It does not
dictate every edit unless the approved design or standard genuinely requires it.

That balance matters for both executors. A human does not want a prompt-shaped script
that removes engineering judgment. An agent should not receive an outcome so vague that
it must invent authority before it can start.

## Project the contract for the executor

The canonical task can be rendered differently without changing meaning:

- a human reads Markdown and follows repository guidance;
- a portable envelope packages bounded context for an external handoff;
- a direct agent run adds provider, transport, mode, timeout, and permission controls; and
- an assurance reviewer reads the same targets and evidence expectations when checking
  the result.

Each projection retains the stable task ID, path, digest, and source identities. An
executor-specific convenience must not create a second version of the task.

## Use the same escalation rule

Both humans and agents discover surprises. A developer may find a hidden consumer; an
agent may encounter an undocumented migration. The task should tell either executor to
preserve evidence, stop the affected work, avoid unrelated edits, and return the proposed
expansion to impact and planning review.

Teams sometimes allow humans to absorb such work informally while constraining agents.
That creates two governance standards. Authorship may influence review depth, but it
should not decide whether product scope can expand without authority.

## Compare claims through independent evidence

A human might report completion in a pull-request description. An agent might return a
structured result. Both are useful claims. Verification asks Git what changed, runs the
required checks, compares expected and unexpected paths, confirms contract and test
artifacts, and records unavailable evidence.

The normalized result contract improves comparison. It does not replace inspection of
the actual repository state. Confidence, seniority, or provider fluency cannot make an
unexpected change disappear.

## Learn from repeated friction

If an experienced developer needs a call to understand every task, the plan is not truly
portable. If every agent searches the same broad directories, repository routing is weak.
If validation repeatedly proves irrelevant behavior, the evidence contract needs review.

Capture those patterns as changes to canonical guidance, graph relationships, task-type
templates, or deterministic tooling. Avoid creating a private “human shortcut” and a
growing provider-specific prompt appendix. One improved contract should benefit every
executor.

## Fair does not mean identical

A security-sensitive agent run may require isolation and a separate assurer. A trusted
maintainer may have broader ambient access. The work contract can remain the same while
execution controls and assurance depth reflect provenance and risk. Equality belongs in
the outcome, scope, and acceptance standard—not necessarily in every operational step.

## Takeaway

Design work so any capable executor can understand the same reviewed contract. Derive
provider packaging from it, preserve one source of authority, and evaluate every result
through the same evidence and acceptance model.

## Canonical CIS sources

- [Task-type contract](../specs/task-type-contract-spec.md)
- [Change impact and bounded planning](../specs/change-impact-and-planning-spec.md)
- [`cis agent prepare`](../manual/cis_agent_prepare.md)
- [Provider-neutral agent execution](../specs/features/agent-execution-coordination-feature.md)

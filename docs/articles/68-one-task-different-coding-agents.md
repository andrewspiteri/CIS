---
title: "Handing One Task to Different Coding Agents"
type: article
status: Active
series: "CIS in Practice"
series_order: 8
owner: "Andrew Spiteri"
last_reviewed: "2026-09-10"
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

`cis agent run` can execute the approved envelope through a configured provider. Each
attempt runs from the same baseline in an isolated worktree, under an explicit transport,
mode, and permission ceiling. Unavailable capabilities and checks are reported rather
than silently substituted.

Run events and results remain evidence, not repository authority. A reviewer inspects the
diff and verification outcome before explicitly importing an accepted result.

## Compare evidence, not prose

Useful comparison dimensions include scope adherence, unexpected files, missing planned
work, validation outcomes, retries, context expansion, elapsed time, and residual risk.

## Keep acceptance independent

The same verification process observes each Git diff. A more confident summary does not
outweigh stronger evidence from another result.

## Learn about the task too

If all providers misunderstand the same boundary, the task or repository knowledge is
likely incomplete. Provider comparison can improve planning rather than merely rank models.

## Define the experiment before running it

Select one approved, eligible, low- or medium-complexity task with meaningful deterministic
evidence. Record the exact task digest, repository snapshot, target, permission ceiling,
mode, timeout, provider versions, transports, and unavailable capabilities. Decide which
comparison measures matter before reading the results.

A task that requires different scopes or permissions for each provider is not a controlled
comparison. It may still test capability, but the conclusion must reflect the changed
contract.

## Diagnose providers explicitly

Check executable resolution, version, authentication status, transport, supported modes,
permission ceiling, continuation, and structured-result behavior. Inconclusive ambient
authentication is different from confirmed failure. CIS should not switch providers or
make permissions broader merely to start a run.

## Seed isolated attempts from one baseline

Workspace-write attempts use separate worktrees created from the same exact commit or
captured tracked and non-ignored untracked snapshot. Each provider receives the full
digest-bound task and bounded context. Local caches may improve performance, but they
should not introduce different canonical inputs.

Record cancellation, retries, resume, and context consolidation as attempt evidence.

## Compare several dimensions

| Dimension | Evidence |
|---|---|
| Scope adherence | Actual Git paths versus approved targets |
| Outcome coverage | Requirements and accepted impacts addressed |
| Validation | Exact checks, valid artifacts, failures, unavailable evidence |
| Safety | Permission requests, containment, secret or network boundaries |
| Efficiency | Elapsed time, attempts, context expansion, bounded usage |
| Maintainability | Diff clarity, local conventions, documentation and tests |
| Honesty | Blockers, residual risk, and proposed scope expansion |

Fluent prose and raw line count are weak ranking measures.

## Keep assurance independent

Use the same verifier and acceptance criteria for every candidate. Ideally the reviewer
does not know which provider produced which diff during the first technical assessment.
A provider's self-review remains supporting evidence.

Importing one result validates its identity and evidence; it does not accept the task.
Candidates not selected remain local evidence and should not be mixed into the governed
checkout accidentally.

## Learn about the system, not only the winner

If every provider misses the same consumer, improve graph or task context. If one
transport repeatedly loses structured results, improve the adapter. If all attempts need
broader permissions, reconsider task decomposition before increasing the ceiling.

The most durable result may be a better work contract and evaluation suite rather than a
permanent provider ranking.

## Takeaway

Compare executors against one canonical work contract and one evidence model. Vary the
provider, not the authority or definition of success.

## Canonical CIS sources

- [Task-type contract](../specs/task-type-contract-spec.md)
- [`cis agent prepare`](../manual/cis_agent_prepare.md)
- [`cis agent run`](../manual/cis_agent_run.md)
- [`cis agent import-result`](../manual/cis_agent_import_result.md)
- [Execution, assurance, diagnostics, and learning](../specs/execution-assurance-and-learning-spec.md)

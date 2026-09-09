---
title: "Bound the Work Before You Give It to an Agent"
type: article
status: Draft
series: "Governed Software Change"
series_order: 6
owner: "Andrew Spiteri"
last_reviewed: "2026-09-08"
review_cadence: on planning or agent-execution change
summary: "How explicit objectives, non-goals, dependencies, validation, and task envelopes make human and agent execution safer."
cis:
  stable_id: change-impact-studio:article:bound-work-before-agent-execution
---

# Bound the work before you give it to an agent

“Implement this feature” is not a task contract. It is a transfer of responsibility.

The executor must discover the scope, infer the architecture, resolve conflicts, decide
what not to change, select validation, and determine when the result is complete. A
senior engineer may navigate those choices well. A coding agent may also produce a
plausible result. In both cases, the plan exists inside the executor rather than in a
reviewable engineering record.

Safe autonomy begins by bounding the work before execution.

## A task needs more than an objective

An objective explains what should become true. It does not explain the authority and
constraints around that outcome.

A CIS work item carries:

- one bounded objective;
- accepted impact and requirement IDs;
- exact source specification paths and digests;
- governed references and applicable standards;
- dependencies and approval gates;
- required changes and explicit non-goals;
- prohibited scope;
- acceptance criteria;
- deterministic, browser, coverage, or assurance validation as applicable;
- expected completion evidence; and
- deferral, residual-risk, and escalation fields.

This makes the task useful outside the conversation that created it. A different agent
or a human can understand what was approved without reconstructing intent from chat.

## Complexity should change the shape of work

Labels such as small, medium, and large often describe estimates without changing the
task contract. CIS uses complexity to constrain execution.

High-complexity work cannot remain one directly executable task. It becomes a parent
that must be decomposed into at least two low- or medium-complexity children. The parent
retains the outcome and coverage relationship; the children define executable boundaries.

This prevents a broad instruction such as “implement authentication across the product”
from being handed to one executor with unrestricted discretion. The work can instead be
split across contracts, permissions, backend behavior, frontend experiences, migration,
verification, and rollout with explicit dependencies.

Decomposition is not about producing more tickets. It is about creating boundaries that
can be reasoned about and verified independently.

## Dependencies carry engineering order

Some tasks are unsafe when executed in parallel. A contract may need approval before
consumers change. A database migration may need to exist before a backfill. Wireframes
and rendered design may require review before frontend implementation. Verification
must observe the candidate implementation rather than race it.

CIS plans dependencies as part of the canonical work contract. Validation rejects
missing references and cycles. The plan orders documentation and contracts, dependencies,
implementation, delivery, and verification without trying to become a general sprint manager.

Approval gates are dependencies too. A task can be technically ready but blocked until
a human resolves a decision, approves a design, or accepts an exception.

## Surface-specific work avoids ambiguous ownership

A logical feature can affect multiple user experiences. CIS classifies frontend work as:

- **public:** anonymous or visitor-facing behavior;
- **customer:** authenticated end-customer behavior; or
- **backoffice:** internal operator and administrative behavior.

When one behavior affects several classifications, planning creates a separate matched
wireframe, design, and implementation chain for each. It does not place “public,
customer, backoffice” in one ambiguous task.

The same principle applies to backend, API, contract, data, security, delivery,
documentation, mobile, and infrastructure surfaces. Structured requirement evidence
activates relevant workstreams; negative statements and coincidental words do not.

Precise classification improves routing, acceptance, and evidence ownership.

## The task envelope is provider-neutral

Once approved, a work item can be prepared as a portable agent envelope. The envelope
binds the canonical task path and digest, context sources, target repository, expected
result fields, and changed-path constraints.

The provider is metadata. The same bounded task can be handed to:

- a human developer;
- GitHub Copilot;
- OpenAI Codex;
- Claude Code; or
- another executor able to consume the contract.

This protects the organization from two forms of lock-in. The task does not depend on a
provider-specific prompt format, and its authority does not live in one agent's memory.

CIS can also execute eligible tasks directly through an explicitly selected Codex or
Claude provider. `cis agent run` negotiates the transport, mode, permission ceiling, and
target; workspace-write execution uses an isolated Git worktree by default. The portable
provider remains preparation-only, and no provider is selected implicitly.

## Stale-safe execution matters

Between preparation and result ingestion, the task or repository can change. An agent
may finish work against an envelope that no longer matches the current approved plan.

CIS validates envelope identity and the current task digest before importing a result.
It checks structured fields and repository-relative changed paths. The result becomes
evidence; it does not automatically transition the task to complete.

Staleness is not an inconvenience to suppress. It is evidence that the executor acted
against a different authority boundary.

## Scope expansion must return to review

Real implementation often discovers missing work. A new consumer appears. A migration
is required. An existing contract contradicts the plan. The correct response is not to
forbid discovery or let the executor quietly absorb it.

A bounded task should instruct the executor to:

1. stop the affected work;
2. preserve the evidence;
3. describe the proposed scope expansion;
4. avoid unrelated implementation; and
5. return the decision to the governed impact and planning workflow.

That preserves both autonomy and control. The executor can identify surprises without
granting itself authority to redefine the change.

## Completion remains separate

An agent result can report changed files, checks, blockers, and residual risks. It can
append useful evidence to the task. It cannot grant completion.

The governed lifecycle still requires plan validation, actual Git comparison,
verification evidence, and the applicable human acceptance. Execution produces a
candidate change, not a verdict about that change.

## Takeaway

Agent autonomy should grow inside explicit engineering boundaries.

Give each executor one bounded objective, exact authority, relevant evidence, clear
non-goals, dependencies, validation, and an escalation path. Decompose high-complexity
work. Bind prepared tasks to current digests. Treat results as evidence rather than
approval.

The quality of agent execution depends heavily on the quality of the work contract
that precedes it.

## Canonical CIS sources

- [Change impact and bounded planning](../specs/change-impact-and-planning-spec.md)
- [Task-type contract](../specs/task-type-contract-spec.md)
- [Core task-type catalogue](../specs/core-task-type-catalog.md)
- [Execution, assurance, diagnostics, and learning](../specs/execution-assurance-and-learning-spec.md)
- [Delivery and assurance](../specs/delivery-and-assurance-spec.md)
- [`cis agent run`](../manual/cis_agent_run.md)
- [Agent provider profile](../references/agent-provider-profile.md)

---
title: "Designing a Provider-Neutral Agent Task"
type: article
status: Draft
series: "Human and Agent Execution"
series_order: 2
owner: "Andrew Spiteri"
last_reviewed: "2026-09-09"
review_cadence: on agent-provider contract change
summary: "The task contract should carry authority and context independently of Codex, Claude, another provider, or a human executor."
cis:
  stable_id: change-impact-studio:article:provider-neutral-agent-task
---

# Designing a provider-neutral agent task

Agent tools change quickly. A durable engineering plan should not depend on one model's
prompt dialect, one CLI, or one editor session.

Provider neutrality begins by making the task—not the provider—the primary contract.

## What the task must contain

A portable task includes:

- stable task and change identity;
- objective and required outcome;
- canonical source paths and digests;
- accepted impact and requirement IDs;
- bounded context and governed references;
- dependencies and approval gates;
- explicit non-goals and prohibited scope;
- target repositories and paths;
- repository revision or working-tree digest;
- run mode, provider selection, permission ceiling, and expiry when execution is direct;
- validation and completion-evidence fields; and
- instructions for blockers, deferrals, and scope expansion.

These fields describe engineering authority without relying on an agent's memory.

## Keep provider details at the edge

Provider metadata can describe preparation format, invocation, transport, capabilities,
permission mediation, continuation, or result adapter. It should not change the meaning
of the task. Codex, Claude, a portable handoff, or a human should receive the same
objective and constraints.

Provider neutrality is capability-aware rather than lowest-common-denominator. CIS can
diagnose executable, version, authentication, transport, permission, and resume support.
An unavailable capability is reported explicitly; CIS does not silently switch transport
or make a run more permissive.

## Use structured results

Result ingestion should expect bounded structured fields: outcome, changed paths,
checks, blockers, residual risk, and proposed expansion. Free-form narration can support
the record but should not replace machine-checkable identity and evidence.

## Do not promise identical behavior

Provider-neutral does not mean every executor has the same capability. One may lack a
browser, model route, credential, or platform. The task declares required evidence; the
executor reports unavailable checks rather than inventing equivalence.

## Neutrality begins with canonical meaning

Provider neutrality is impossible if important instructions exist only in a provider
prompt. The objective, accepted impact, decisions, non-goals, targets, and evidence must
live in the repository-owned task. An envelope is a projection of that contract, bound to
its path and digest.

This design also supports human execution. A developer can open the same work item and
understand what the provider would receive without decoding a generated prompt.

## Separate three layers

A durable integration distinguishes:

| Layer | Responsibility |
|---|---|
| Canonical task | Engineering outcome, authority, scope, dependencies, validation, evidence |
| Execution envelope | Digest-bound projection, bounded context, provider-neutral result schema |
| Provider adapter | Transport, invocation, capability negotiation, streaming, cancellation, normalization |

Changing the adapter should not change the first layer. If a provider requires a special
instruction to respect a product rule, that rule belongs in the task or applicable
standard and can then be rendered appropriately for every executor.

## Capabilities should fail visibly

One provider may support continuation, structured events, and mediated file requests;
another may support only a non-interactive process; the portable route may prepare an
envelope without executing it. CIS diagnoses those differences before the run.

The controller should reject an unsupported transport or permission rather than silently
falling back to a less observable or more powerful mode. A required browser check that
cannot run becomes unavailable evidence. It does not become “approximately covered” by a
model explanation.

## Permissions are ceilings

Read-only, workspace-write, and any future permission classes describe the maximum action
the provider may perform, not an assurance that every allowed action is appropriate.
Workspace-write remains contained to the isolated target worktree. A provider cannot
convert the ceiling into permission to change dependencies, approve requests, or use the
network unless those capabilities are explicitly supported and authorized.

Provider-native credentials stay outside the task and envelope. Authentication status is
diagnostic evidence, not a reason to copy tokens into repository configuration.

## Normalize results without erasing detail

Every adapter should produce the same bounded result concepts: outcome, changed paths,
checks, blockers, residual risks, proposed expansion, run identity, and artifact references.
Provider-specific events may be retained in local evidence, but canonical import depends
on the normalized contract.

Malformed or missing expected output is `InvalidEvidence` even when the process exits
zero. Conversely, a failed attempt can still supply useful diagnostics. Process state and
evidence validity answer different questions.

## Portability has an honest limit

Two providers can receive the same authority and still produce different work. Models,
tools, context windows, and platform capabilities differ. Provider neutrality guarantees
a common contract and comparable evidence—not identical reasoning or output.

That is enough to make substitution governable. A team can compare attempts against the
same task, improve the task when every provider misses the same boundary, and change tools
without losing its engineering record.

## Takeaway

Keep intent, scope, constraints, and evidence in a stable task contract. Treat providers
as replaceable execution adapters. Portability protects both governance and future tool choice.

## Canonical CIS sources

- [Task-type contract](../specs/task-type-contract-spec.md)
- [Execution, assurance, diagnostics, and learning](../specs/execution-assurance-and-learning-spec.md)
- [`cis agent prepare`](../manual/cis_agent_prepare.md)
- [`cis agent providers`](../manual/cis_agent_providers.md)
- [Agent provider profile](../references/agent-provider-profile.md)
- [Provider-neutral agent execution](../specs/features/agent-execution-coordination-feature.md)

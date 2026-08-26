---
title: "Designing a Provider-Neutral Agent Task"
type: article
status: Draft
series: "Human and Agent Execution"
series_order: 2
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: on agent-provider contract change
summary: "The task contract should carry authority and context independently of Codex, Copilot, Claude, or a human executor."
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
- validation and completion-evidence fields; and
- instructions for blockers, deferrals, and scope expansion.

These fields describe engineering authority without relying on an agent's memory.

## Keep provider details at the edge

Provider metadata can describe preparation format, invocation, or result adapter. It
should not change the meaning of the task. Codex, Copilot, Claude, or a human should
receive the same objective and constraints.

## Use structured results

Result ingestion should expect bounded structured fields: outcome, changed paths,
checks, blockers, residual risk, and proposed expansion. Free-form narration can support
the record but should not replace machine-checkable identity and evidence.

## Do not promise identical behavior

Provider-neutral does not mean every executor has the same capability. One may lack a
browser, model route, credential, or platform. The task declares required evidence; the
executor reports unavailable checks rather than inventing equivalence.

## Takeaway

Keep intent, scope, constraints, and evidence in a stable task contract. Treat providers
as replaceable execution adapters. Portability protects both governance and future tool choice.

## Canonical CIS sources

- [Task-type contract](../specs/task-type-contract-spec.md)
- [Execution, assurance, diagnostics, and learning](../specs/execution-assurance-and-learning-spec.md)
- [`cis agent prepare`](../manual/cis_agent_prepare.md)
- [`cis agent providers`](../manual/cis_agent_providers.md)


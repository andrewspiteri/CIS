---
title: "Planning Across Frontend, Backend, Data, Security, and Operations"
type: article
status: Draft
series: "Change Impact and Planning"
series_order: 6
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: on task-decomposition change
summary: "How structured requirement evidence activates distinct workstreams and preserves ownership across a complete feature."
cis:
  stable_id: change-impact-studio:article:planning-across-engineering-surfaces
---

# Planning across frontend, backend, data, security, and operations

A feature is often described through its most visible surface. “Add invitations” sounds
like a screen. “Expose search” sounds like an API. The complete change can involve
contracts, permissions, persistence, migrations, infrastructure, observability, rollout,
and documentation.

## Use structured requirement evidence

CIS feature requirements identify surfaces such as frontend, backend, API, contract,
data, security, delivery, mobile, infrastructure, and documentation.

Planning activates a workstream from positive structured evidence. A section that says
“no database migration is required” should not trigger a migration task merely because
the words appear.

## Preserve distinct obligations

Each surface has different acceptance evidence:

- frontend needs states, interaction, accessibility, and browser behavior;
- API work needs operations, errors, security, consumers, and compatibility;
- persistence needs schema, consistency, transaction, and migration evidence;
- security needs threat, permission, secret, and abuse boundaries;
- operations need telemetry, failure behavior, rollback, and support evidence.

Combining them into one task makes completion ambiguous.

## Order contracts and dependencies

Documentation and contract decisions should precede dependent implementation. Data or
infrastructure prerequisites precede consumers. Verification follows the candidate
implementation and observes all affected repositories.

## Route work to repository owners

In a workspace, participant roles route tasks to frontend, backend, database, and
infrastructure repositories. Cross-cutting coordination remains workspace-wide. When
classification evidence is absent, the plan conservatively retains declared targets.

## Cross-surface signals increase assurance

A change that crosses trust, data, contract, and deployment boundaries deserves wider
validation than a local implementation correction. Complexity and assurance should
reflect the surfaces involved.

## Takeaway

Plan the complete engineering outcome, not only the most visible implementation. Use
structured evidence to activate workstreams, keep their obligations distinct, order
their dependencies, and route them to the right owners.

## Canonical CIS sources

- [Change impact and bounded planning](../specs/change-impact-and-planning-spec.md)
- [Core task-type catalogue](../specs/core-task-type-catalog.md)
- [Task-type contract](../specs/task-type-contract-spec.md)
- [Module ownership map](../references/module-ownership-map.md)


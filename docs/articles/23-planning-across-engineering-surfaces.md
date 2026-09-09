---
title: "Planning Across Frontend, Backend, Data, Security, and Operations"
type: article
status: Draft
series: "Change Impact and Planning"
series_order: 6
owner: "Andrew Spiteri"
last_reviewed: "2026-09-09"
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

CIS keeps a common spine around those conditional workstreams. Coordination, documentation
and contracts, verification, independent assurance, and final delivery are always created;
the remaining core task types activate from positive feature evidence. This preserves the
whole-change obligations even when only one implementation surface appears obvious.

## Order contracts and dependencies

Documentation and contract decisions should precede dependent implementation. Data or
infrastructure prerequisites precede consumers. Verification follows the candidate
implementation and observes all affected repositories.

## Route work to repository owners

In a workspace, product-owned repository roles route tasks to frontend, backend,
database, and infrastructure repositories. Dependencies remain read-only context;
their implementation requires a separately governed product change. Cross-cutting
coordination remains workspace-wide. When classification evidence is absent, the plan
conservatively retains declared owned targets.

## Cross-surface signals increase assurance

A change that crosses trust, data, contract, and deployment boundaries deserves wider
validation than a local implementation correction. Complexity and assurance should
reflect the surfaces involved.

## Translate one outcome into several obligations

Consider “let a customer invite a friend to a list.” The visible interaction is a form,
but the complete surface map may include:

| Surface | Example obligation | Representative evidence |
|---|---|---|
| Product and documentation | Define invitation lifecycle and limits | Approved requirements and contract rows |
| Customer experience | Issue, copy, revoke, expire, and recover from errors | Wireframes, design review, browser tests |
| API | Publish operations and stable problem responses | Inventory, OpenAPI, compatibility diff |
| Backend | Enforce ownership, expiry, and idempotency | Focused unit and integration tests |
| Data | Store token digest, expiry, revocation, and audit state | Schema and migration verification |
| Security | Prevent forwarding abuse and private-data disclosure | Permission and security evidence |
| Operations | Observe issue/redeem failures and cleanup lag | Telemetry and failure-path checks |
| Delivery | Sequence configuration, migration, and rollout | Workflow and rollback evidence |

The plan does not need a task for every table row. It does need an accountable place for
every accepted obligation.

## Negative requirements prevent false activation

Structured evidence matters because prose contains negation, examples, and alternatives.
“The first release will not send email” should constrain scope, not create an email task.
“Evaluate whether a migration is required” is a decision or discovery activity, not proof
that migration work is approved.

Planning should preserve the source requirement ID and its positive or negative meaning.
Keyword occurrence alone is not an authority signal.

## Sequence by contract and reversibility

A common safe order is to settle product and contract meaning before implementing
consumers, establish data and infrastructure prerequisites before dependent runtime work,
and verify the integrated behavior before rollout. Reversible discovery and design can
often proceed earlier than irreversible data or compatibility changes.

Dependencies should explain this engineering order. “Task B depends on Task A” is more
useful when the plan states that B consumes the contract or schema approved by A.

## Preserve surface ownership without creating silos

Separate workstreams do not mean independent mini-features. Shared stable requirements,
decisions, contracts, and accepted impact connect them. The customer implementer should
not redefine an API error, and the backend implementer should not invent the user recovery
flow. Questions return to their owning work or decision.

Cross-cutting coordination watches the outcome across all surfaces. It does not replace
the specialist evidence each surface requires.

## Plan failure and transition states

Happy-path decomposition is usually incomplete. Data may be partly migrated, a provider
may be unavailable, a cache may be stale, a client may use an older contract, or telemetry
may fail. The affected workstreams should state how those states behave and how they are
verified.

Rollout and rollback are product behavior when users can observe them. They should not be
left to a final deployment checklist after implementation choices have made recovery
impossible.

## Make final assurance integrate the surfaces

Focused tests prove local claims. Final verification checks that the accepted impact has
coverage, the actual Git diff matches repository targets, contracts agree across
consumers, required evidence is usable, and residual risk is explicit. Cross-surface
complexity should increase the breadth of this integrated check.

## Takeaway

Plan the complete engineering outcome, not only the most visible implementation. Use
structured evidence to activate workstreams, keep their obligations distinct, order
their dependencies, and route them to the right owners.

## Canonical CIS sources

- [Change impact and bounded planning](../specs/change-impact-and-planning-spec.md)
- [Core task-type catalogue](../specs/core-task-type-catalog.md)
- [Task-type contract](../specs/task-type-contract-spec.md)
- [Module ownership map](../references/module-ownership-map.md)

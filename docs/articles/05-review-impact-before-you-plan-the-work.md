---
title: "Review Impact Before You Plan the Work"
type: article
status: Active
series: "Governed Software Change"
series_order: 5
owner: "Andrew Spiteri"
last_reviewed: "2026-09-22"
review_cadence: on impact or planning change
summary: "Why evidence-backed impact findings must remain proposals until reviewed against an exact baseline."
cis:
  stable_id: change-impact-studio:article:review-impact-before-planning
---

# Review impact before you plan the work

Software planning often begins too late in the reasoning process. A request is converted
directly into tasks, estimates, or an agent prompt before the team has established what
the change may affect.

The resulting plan can be detailed and still be incomplete. It may describe the API and
backend implementation precisely while omitting a consumer, permission, migration,
runbook, or compatibility obligation that nobody looked for.

Task decomposition should not be the first act of planning. Impact review should.

## A change needs an exact starting point

Impact is relative to a baseline. The same request can have different consequences on
two commits, two graph generations, or two versions of a supported contract.

Change Impact Studio creates a repository-owned change dossier against an exact Git
commit or graph build. The proposal records:

- the intended outcome;
- initial impact roots;
- constraints and boundaries;
- outcome-level acceptance criteria; and
- the baseline that gives every finding its context.

Analysis refuses to silently switch to a different graph build. If the repository's
governed inputs change, the team must rebuild, rebaseline within strict limits, or
create a new change. Reviewed scope cannot drift merely because the tool was rerun later.

## Findings are proposals, not scope

CIS performs bounded graph traversal from the selected roots and emits stable impact
findings. A finding identifies a target, category, rationale, evidence, confidence, and
review state.

Typical categories include:

- requirements and documentation;
- architecture and decisions;
- APIs and other contracts;
- dependencies and packages;
- frontend, backend, data, and infrastructure;
- security and permissions;
- tests and workflows;
- operations and observability; and
- agent guidance.

The analyser can propose that a concern is affected. It cannot decide that the concern
belongs in approved scope. That distinction matters because the graph may contain
false positives, missing relationships, or ambiguous evidence.

Human review dispositions each finding as:

- **accepted:** must be covered by bounded work;
- **rejected:** reviewed and excluded with a reason;
- **deferred:** acknowledged but unresolved outside the current approved plan; or
- **proposed:** still awaiting review.

Re-analysis preserves stable identities and existing rationale for unchanged targets.
The team can improve the evidence without losing earlier decisions.

## Completeness is a bounded result

No repository graph can prove that every semantic impact exists. Some obligations live
in stakeholder knowledge, production behavior, vendor contracts, or undocumented
assumptions.

CIS therefore defines completeness narrowly. Planning readiness requires:

- at least one discovered and accepted finding;
- no findings still proposed;
- no deferred findings hidden outside approved scope; and
- no truncated traversal.

This does not claim omniscience. It says that every finding produced within the declared
bounds has been reviewed and that the bounds themselves are visible.

If the result is too narrow, a reviewer can broaden roots or traversal limits and rerun
the idempotent analysis. Honest uncertainty is more valuable than a green status that
implies the impossible.

## Decisions emerge from impact

Impact review often discovers questions that cannot be answered by more searching:

- Should compatibility be preserved or should the contract be versioned?
- Is a data migration required?
- Which repository owns a shared capability?
- Does the feature need public, customer, and backoffice experiences?
- Can a remote model receive the evidence?
- What should happen when a cache is stale or unavailable?

Those questions belong in a decision record, not in an implementer's private reasoning.
A change-local decision records stable identity, options, evidence, blocking gate,
status, resolution, and human rationale. Open or deferred blocking decisions prevent
plan approval.

Durable decisions can later be promoted into architecture decision records without
losing their change origin.

## Planning consumes accepted impact only

Once impact is reviewed and decisions are ready, planning can be deterministic and
bounded. Every accepted finding must appear in at least one work item. A work item may
not cite an unaccepted finding as though it were approved scope.

That produces a traceable chain:

```text
Change outcome
  → impact root
  → evidence-backed finding
  → human disposition
  → bounded work item
  → validation and completion evidence
```

If a finding is missing from the plan, validation fails. If a task refers to scope the
reviewer never accepted, validation also fails.

This prevents task generation from becoming an unreviewed scope-expansion mechanism.

## Authority can be reused when it is exact

Manual disposition is not always necessary for every finding. When a current approved
feature specification already carries explicit human authority, CIS can deterministically
derive eligible findings and carry that exact authority into a validated plan.

The shortcut is deliberately narrow. It stops on low confidence, truncation, deferred
impact, stale approval, mismatched scope, provider conflicts, unresolved decisions, or
invalid generated work. It preserves the original reviewer, rationale, path, and digest.

This is not autonomous approval. It is provenance reuse: do not ask a human to approve
the same unchanged meaning twice, but do ask whenever the meaning or risk expands.

## Worked contrast: review invitation impact before creating tasks

The following finding set is hypothetical. It demonstrates disposition and planning
readiness rather than literal CIS output.

### Planning directly from the request

“Invite a friend to a list” becomes three tasks:

1. add an API endpoint;
2. add invitation storage; and
3. add a customer screen.

The list is easy to estimate, but it reflects only the first implementation shape that
came to mind. It does not show whether permissions, expiry cleanup, problem contracts,
abuse controls, audit events, infrastructure, or documentation were considered. A later
workflow change will look like executor overreach even if it was a necessary consequence
that planning failed to discover.

### Reviewing proposed impact first

Bounded analysis starts from the approved invitation outcome and exact workspace
baseline. It proposes findings with evidence, after which a reviewer records dispositions:

| Proposed finding | Review outcome | Reason |
|---|---|---|
| Customer invitation and error states | Accepted | Required by the approved customer outcome |
| API contract and problem responses | Accepted | The customer flow crosses a governed contract |
| Authorization and list ownership | Accepted | Only an authorized list owner may issue or revoke access |
| Persistence, expiry, and cleanup | Accepted | Link lifecycle requires durable state and expiry behavior |
| Audit and abuse-control evidence | Accepted | Security and operational constraints require reviewable behavior |
| Public marketing page | Rejected | A text match on “invite” does not make the page part of this feature |
| Deployment workflow | Proposed | Evidence suggests a new setting or schedule, but ownership and need remain unresolved |

Planning remains blocked while the workflow finding is still proposed. The reviewer can
broaden analysis, reject it with evidence, or accept it and require infrastructure work.
Only the accepted findings become task obligations; the rejected match remains recorded
so that it is not rediscovered as unexplained scope later.

The governed plan may still contain three tasks, or it may contain ten. Its value comes
from showing why each task belongs and which investigated concerns do not.

## Takeaway

Do not turn a request directly into tasks.

Bind the change to an exact baseline. Propose impact with evidence. Keep uncertainty
visible. Let humans confirm scope and resolve decisions. Only then convert accepted
impact into bounded work.

A plan is trustworthy when it can show not only what will be done, but why every item
belongs and who accepted the obligation.

## Canonical CIS sources

- [Change impact and bounded planning](../specs/change-impact-and-planning-spec.md)
- [Context model and local graph](../specs/context-model-and-graph-spec.md)
- [Technical-intent governance](../specs/technical-intent-governance-spec.md)
- [Traceability matrix specification](../specs/traceability-matrix-spec.md)

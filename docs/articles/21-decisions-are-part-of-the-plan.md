---
title: "Decisions Are Part of the Plan"
type: article
status: Draft
series: "Change Impact and Planning"
series_order: 4
owner: "Andrew Spiteri"
last_reviewed: "2026-09-09"
review_cadence: on decision-lifecycle change
summary: "Why unresolved choices, alternatives, evidence, and rationale belong in governed change state before dependent tasks execute."
cis:
  stable_id: change-impact-studio:article:decisions-part-of-plan
---

# Decisions are part of the plan

Many plans list tasks while hiding the choices that determine them. “Implement API
changes” may depend on a compatibility strategy. “Add storage” may depend on consistency
and migration decisions. “Build the screen” may depend on unresolved user states.

When those choices are absent from the plan, the executor becomes the decision maker.

## Capture the question before the answer

A CIS change decision records:

- stable ID and category;
- the exact question;
- at least two recorded options;
- evidence and constraints;
- blocking or advisory authority;
- the gate before which it is required;
- lifecycle state; and
- human resolution or deferral rationale.

Recording alternatives prevents hindsight from making the chosen option look inevitable.

## Blocking and advisory are different

A blocking decision must be resolved before its gate. Compatibility strategy, trust
boundary, data migration, or provider selection can make downstream work unsafe.

An advisory decision can remain visible without stopping unrelated delivery. Its state
still appears in validation so it cannot be forgotten.

## Resolution needs exact choice and rationale

Resolution selects one recorded option. A free-form answer that does not match an option
would erase the comparison that was reviewed. Replacing a resolved choice requires a
superseding decision rather than silently editing history.

Models may propose questions and options. They cannot resolve or defer them.

## Durable choices can become ADRs

Some decisions only apply to one change. Others establish long-lived architecture. CIS
can promote a resolved decision into a catalogued ADR with alternatives, evidence,
rationale, and originating change provenance.

Promotion preserves the local decision instead of creating an unrelated architecture
record after the fact.

## Decisions shape task dependencies

Work that relies on a decision should depend on its gate. An implementation task should
not start while the underlying contract or ownership choice is open.

This makes the plan an authority graph, not merely an ordered list.

## Distinguish a decision from an implementation detail

Not every choice deserves a governed decision. Local names, equivalent refactorings, and
reversible mechanics can remain with the executor when the task and standards allow it.
A decision record is warranted when alternatives change product behavior, public
compatibility, ownership, security, data, operations, or the evidence needed for
acceptance.

A useful test is to ask whether two reasonable implementations could satisfy the task
while producing materially different consequences. If so, the plan probably contains an
unresolved decision.

## Follow a compatibility decision

Suppose an API response property must become mandatory. The options might be:

1. retain the optional field and validate only new writes;
2. introduce an additive replacement field and migration window; or
3. release a new contract version and migrate consumers.

The evidence includes supported baselines, consumer inventory, data quality, rollout
capacity, and deprecation policy. The decision identifies which gate it blocks—contract
approval, for example—and records who selected the option and why. Only then can backend,
client, migration, documentation, and monitoring work be planned coherently.

Without that record, each executor may make a locally sensible but incompatible choice.

## Gates turn rationale into delivery order

A blocking decision should name the earliest work that cannot safely proceed. It may
allow discovery or a prototype while preventing contract publication or implementation.
This is more precise than freezing the entire change whenever one question remains open.

Tasks that consume the decision should carry its stable ID and resolved digest. If the
decision is later superseded, plan validation can identify the dependent work that became
stale. The relationship is explicit rather than buried in a meeting note.

## Deferral is also a decision

Deferring a choice can be legitimate when the current change has a safe bounded path.
The deferral needs an authority, rationale, consequence, and future review condition. A
blocking decision cannot be relabelled advisory merely to make validation green.

For example, choosing not to automate key rotation in the first release may require a
manual operating procedure and an acceptance risk. The plan must cover those compensating
obligations rather than treating deferral as absence.

## Preserve supersession

Resolved decisions should not be edited until the history looks clean. When new evidence
changes the choice, create a superseding record that explains what moved. Existing tasks
and ADRs can then point to the decision that actually governed them.

Promotion to an ADR is appropriate when the choice controls future changes beyond the
current dossier. The ADR does not erase the change-local record; it gives the durable
architecture a stable home and retains origin, alternatives, and rationale.

## Takeaway

A task plan is incomplete when its material choices remain implicit. Record questions,
options, evidence, gates, and rationale before execution. Let tools propose alternatives,
but keep resolution with the human authority that owns the consequence.

## Canonical CIS sources

- [Change impact and bounded planning](../specs/change-impact-and-planning-spec.md)
- [`cis decision create`](../manual/cis_decision_create.md)
- [`cis decision resolve`](../manual/cis_decision_resolve.md)
- [`cis decision promote`](../manual/cis_decision_promote.md)

---
title: "Decisions Are Part of the Plan"
type: article
status: Draft
series: "Change Impact and Planning"
series_order: 4
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
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

## Takeaway

A task plan is incomplete when its material choices remain implicit. Record questions,
options, evidence, gates, and rationale before execution. Let tools propose alternatives,
but keep resolution with the human authority that owns the consequence.

## Canonical CIS sources

- [Change impact and bounded planning](../specs/change-impact-and-planning-spec.md)
- [`cis decision create`](../manual/cis_decision_create.md)
- [`cis decision resolve`](../manual/cis_decision_resolve.md)
- [`cis decision promote`](../manual/cis_decision_promote.md)

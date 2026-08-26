---
title: "Why CIS Cannot Self-Approve Its Improvements"
type: article
status: Draft
series: "Product Evolution and Learning"
series_order: 4
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: on learning-authority change
summary: "A tool that measures its workflow may propose changes, but approving its own source, policy, or acceptance criteria would collapse governance."
cis:
  stable_id: change-impact-studio:article:cis-cannot-self-approve
---

# Why CIS cannot self-approve its improvements

A system that observes failures can often suggest how to prevent them. Allowing it to
apply those suggestions automatically appears to close the learning loop.

It also lets the system change the rules by which it judges itself.

## Diagnosis is not authority

Repeated failures, high output, missing relationships, and unexpected changes are
evidence. A model or deterministic rule can formulate a candidate improvement.

## Local optimization can weaken control

Removing a review step may improve speed while bypassing a real risk decision. A new
instruction may conflict with a standard. A token-saving change may omit required context.

## Proposals remain derived

CIS learning proposals stay in local state with source digest and evidence. Human review
records the recommendation, reviewer, rationale, and timestamp in canonical history.

## Product changes use normal governance

Applying learning history does not rewrite source or policy. The approved recommendation
becomes input to a new governed change with impact, plan, implementation, and verification.

## Takeaway

Let CIS identify and explain improvement opportunities. Do not let it grant authority to
the source, standards, or acceptance changes it proposes.

## Canonical CIS sources

- [Execution, assurance, diagnostics, and learning](../specs/execution-assurance-and-learning-spec.md)
- [Learning history](../references/learning-history.md)
- [`cis learn review`](../manual/cis_learn_review.md)

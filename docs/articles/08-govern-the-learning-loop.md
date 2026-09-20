---
title: "Govern the Learning Loop"
type: article
status: Active
series: "Governed Software Change"
series_order: 8
owner: "Andrew Spiteri"
last_reviewed: "2026-09-10"
review_cadence: on diagnostics, feedback, or learning change
summary: "How delivery evidence can improve future engineering work without allowing a tool or model to rewrite canonical authority."
cis:
  stable_id: change-impact-studio:article:govern-the-learning-loop
---

# Govern the learning loop

Every software change teaches the team something.

A missing impact reveals a weak relationship in repository knowledge. Repeated broad
searches reveal poor routing. A surprise workflow edit reveals an incomplete planning
rule. A recurring validation failure reveals an unclear task contract. An unnecessary
model call reveals work that could be deterministic.

The difficult part is not collecting observations. It is turning them into useful
improvement without allowing the delivery system to silently rewrite its own rules.

## Learning needs evidence

Retrospectives often produce good intentions that are hard to trace to delivery facts.
CIS captures bounded local evidence about how its own commands and workflows are used.

The feedback ledger can record:

- command path and option names;
- elapsed time and outcome;
- stdout and stderr sizes;
- estimated output tokens;
- defensible possible-savings estimates; and
- repeated failures or high-output opportunities.

It deliberately excludes option values, source content, prompts, responses, credentials,
and command output. The objective is to understand the workflow without constructing a
second repository-content store.

Diagnostics follow a similar boundary. Sources must be explicitly enabled, bounded,
and non-sensitive. Reads are redacted defensively, and only normalized derived analysis
is retained.

## Estimates must show their basis

Claims about AI cost or token savings are easy to exaggerate. CIS requires a calculation
basis and confidence.

If a command has no defensible counterfactual, its possible savings remain zero with
confidence `none`. A compact context pack can compare included excerpts with the complete
selected sources. An index search can compare routing output with the estimated size of
matched files. Those estimates are directional, not provider invoices.

This distinction matters because a feedback system can otherwise optimize for impressive
numbers rather than improved delivery.

## Surprise is a learning opportunity

Suppose verification discovers that a CI workflow changed even though delivery was not
part of the approved impact.

The immediate change decision may be to accept the workflow update, revise the plan, or
remove it. The longer-term question is different:

> Why did the impact analysis and task contract fail to anticipate this relationship?

A useful learning proposal might suggest:

- adding a reviewed relationship between the affected component and workflow;
- updating a planning trigger;
- improving a repository profile;
- adding a verification obligation;
- clarifying agent guidance; or
- creating a focused deterministic check.

The proposal should cite the evidence that produced it. Without provenance, “improve
the workflow” is only another unbounded instruction.

## Proposals are not self-modification

An adaptive engineering tool is tempting: observe failures, rewrite instructions, tune
the rules, and apply the improvement automatically.

That design collapses evidence, interpretation, and authority. A model can misdiagnose
the cause. A local optimization can weaken a broader policy. A new instruction can
conflict with a standard. A rule that reduces token usage can reduce assurance quality.

CIS keeps learning proposals in derived local state until a human reviews them. Applying
a proposal promotes only the approved recommendation, reviewer, rationale, timestamp,
and source digest into canonical learning history.

Application does not autonomously modify:

- product source;
- specifications or standards;
- agent instructions or skills;
- approvals or acceptance records; or
- provider and security policy.

Those changes remain normal governed work with their own impact and validation.

## Learning should improve the next change

The value of a learning loop appears in future delivery. Reviewed improvements can make
the next task safer or smaller by:

- routing context more accurately;
- discovering affected contracts earlier;
- selecting a deterministic command instead of a model;
- generating a better bounded task;
- applying a relevant standard automatically;
- identifying the right focused test;
- reducing unplanned changes; or
- surfacing a known decision before implementation.

This creates a controlled cycle:

```text
Plan
  → execute
  → verify
  → observe
  → propose learning
  → human review
  → governed improvement
  → better future plan
```

The cycle improves the engineering system without granting it the authority to define
its own success.

## Do not optimize away judgment

Some friction is accidental. Repeated searches, duplicate approvals of unchanged
content, excessive output, and avoidable model calls should be reduced.

Other friction represents a real decision. Compatibility, privacy, risk acceptance,
architecture ownership, and exceptions should not be automated merely because they
slow a workflow.

A governed learning loop asks whether a step is redundant or authoritative. It
automates preservation and derivation while protecting the points where human meaning
enters the system.

## Takeaway

Delivery evidence should improve the next delivery cycle, but observation is not
authority and a proposal is not policy.

Collect bounded, sanitized evidence. Preserve the basis and confidence of estimates.
Turn repeated failures and surprises into traceable proposals. Require human review
before learning becomes canonical. Apply resulting changes through the same governed
workflow they are intended to improve.

The safest learning system is not one that changes itself fastest. It is one that can
show what it learned, why, and who authorized the consequence.

## Canonical CIS sources

- [Feedback loop](../specs/feedback-loop-spec.md)
- [Execution, assurance, diagnostics, and learning](../specs/execution-assurance-and-learning-spec.md)
- [Diagnostics profile](../references/diagnostics-profile.md)
- [Learning history](../references/learning-history.md)
- [Product intent](../specs/product-intent-spec.md)

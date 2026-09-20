---
title: "Why CIS Cannot Self-Approve Its Improvements"
type: article
status: Active
series: "Product Evolution and Learning"
series_order: 4
owner: "Andrew Spiteri"
last_reviewed: "2026-09-10"
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
The same rule applies when the evidence came from a directly executed agent run: execution
can produce a result, but it cannot grant that result authority.

## Product changes use normal governance

Applying learning history does not rewrite source or policy. The approved recommendation
becomes input to a new governed change with impact, plan, implementation, and verification.

## Self-measurement creates an incentive problem

If CIS can change the checks used to judge reliability, it can improve its metrics by
weakening the checks. If it can alter routing policy, it can reduce reported model cost by
omitting useful analysis. If it can approve source changes, a diagnosis can become its own
evidence of success.

No malicious intent is required. Local optimization and incomplete evidence are enough to
produce the conflict.

## Keep the loop in separate stages

```text
Observe bounded evidence
  → normalize and correlate
  → propose one improvement
  → human review with rationale
  → record approved learning
  → create a governed product change
  → implement and verify independently
```

Each arrow changes the authority of the material. A derived observation becomes a
proposal; a reviewed proposal becomes learning input; a separate change modifies the
product. Skipping stages hides who authorized the consequence.

## Direct agent execution does not change the rule

An agent can implement a task in an isolated worktree and return valid structured
evidence. It cannot import its own result as accepted completion, revise the plan to fit
its diff, approve an exception, or promote a lesson that makes the work valid.

The controller enforces permissions and evidence shape. Human authorities still control
scope, exceptions, learning, and acceptance.

## Human review should challenge causality

A repeated failure may come from unclear documentation, a product defect, provider
instability, stale packaging, a test environment, or incorrect usage. The proposal should
cite bounded evidence and alternatives. The reviewer asks whether the recommendation
addresses the cause and whether it weakens another control.

“The model recommends it” is not rationale.

## Approved learning is not an automatic patch

Recording “add installed-package smoke coverage for provider-owned routes” in learning
history does not edit the release workflow. The follow-up change must analyse impact,
resolve design, implement tests and documentation, and produce verification. Learning
informs authority; it does not bypass delivery governance.

## Preserve rejected proposals

A rejected recommendation can remain derived evidence with rationale so the same pattern
is not proposed repeatedly without new facts. Rejection also helps evaluate whether future
diagnostics improve. It should not become a hidden rule that similar ideas can never be
considered again.

## Audit the separation itself

Tests should prove that proposal commands write only derived state, review commands require
human identity and rationale, application records learning without editing product source,
and subsequent implementation uses the normal change lifecycle. The extension and agent
interfaces must not introduce a shortcut that the CLI forbids.

This boundary is architectural. A helpful automation button that combines proposal,
approval, and source mutation would reintroduce self-approval even if each underlying
command remained individually safe.

## Takeaway

Let CIS identify and explain improvement opportunities. Do not let it grant authority to
the source, standards, or acceptance changes it proposes.

## Canonical CIS sources

- [Execution, assurance, diagnostics, and learning](../specs/execution-assurance-and-learning-spec.md)
- [Learning history](../references/learning-history.md)
- [`cis learn review`](../manual/cis_learn_review.md)

---
title: "Independent Assurance for AI-Assisted Development"
type: article
status: Draft
series: "Verification and Assurance"
series_order: 8
owner: "Andrew Spiteri"
last_reviewed: "2026-09-09"
review_cadence: on assurance or agent change
summary: "How independent evidence and risk ownership let teams use capable coding agents without trusting self-assessment."
cis:
  stable_id: change-impact-studio:article:independent-assurance-ai-development
---

# Independent assurance for AI-assisted development

Coding agents can implement and self-review quickly. That makes independent assurance
more—not less—important.

## Independence is about evidence sources

The assurer should inspect the approved task, actual Git diff, deterministic checks,
contract evidence, and residual risk rather than relying only on the agent's summary.

The assurer can be a human supported by tools. It does not always require a separate
team, but high-risk work benefits from a distinct task and reviewer.

## Models can assist but not certify

A second model may identify suspicious scope, missing tests, or inconsistent behavior.
Agreement between models is still advisory. Deterministic evidence and human authority
remain necessary for compliance, exceptions, risk, and acceptance.

Where CIS uses agents for authoring and review, provider separation is explicit. For
example, BRD review must use a provider different from the latest successful producer,
and bounded revision is followed by a different-provider closure review. Provider
difference improves independence; it does not turn model agreement into approval.

## Assurance should challenge the plan too

The implementation may match an incomplete plan. Independent review should ask whether
accepted impacts, validation, and operational consequences were adequate, not only
whether the agent followed instructions.

## Preserve residual risk

Known limitations, skipped checks, and deferred findings remain in the acceptance record.
Automation cannot convert their absence into confidence.

## Independence is not the number of models

Running the same task through two models can reveal disagreement, but shared context,
assumptions, and missing requirements can lead both to the same wrong conclusion. Genuine
independence comes from different evidence sources, a fixed approved contract, and a
reviewer who does not need to defend the implementation.

A second provider is useful as an adversarial reader. It is not a substitute for Git,
tests, contract tools, security evidence, or human risk ownership.

## Begin with the approved claim

The assurer needs the original proposal, baseline, accepted impact, decisions, bounded
tasks, non-goals, and evidence requirements. Reviewing only the final diff asks whether
the code looks plausible; it does not ask whether the right outcome was delivered.

The task should make assurance possible without access to a private agent conversation.
If essential reasoning exists only there, the engineering contract is incomplete.

## Observe the candidate independently

The assurance process should:

1. compare actual Git state with each approved repository baseline;
2. inspect missing, unexpected, renamed, deleted, and untracked paths;
3. validate source and contract digests;
4. run or reconcile the required focused and wider checks;
5. examine invalid, unavailable, and failed evidence separately;
6. challenge security, data, compatibility, and operational assumptions; and
7. record findings and residual risk without altering the executor's report.

The agent summary can explain intent and difficulties. It does not control the observed
facts.

## Scale independence with consequence

A low-risk documentation correction may be assured by strict validation and a reviewer
who inspects the diff. A permission boundary may require a security specialist, mutation
or negative-path evidence, and a distinct assurance task. A release change may require
installed-artifact smoke testing and rollback evidence.

Independence should be proportionate, not ceremonial. Requiring another team for every
small change can dilute attention; letting the executor self-certify every high-risk
change concentrates authority dangerously.

## Challenge omissions in the plan

Perfect conformance to an incomplete plan is still incomplete delivery. The assurer
should ask whether impact traversal was bounded honestly, dependencies were available,
public contracts and consumers were considered, negative behavior was tested, and the
evidence contract matches the risk.

When a missing obligation is found, preserve it as a verification finding and return the
scope decision to governance. Do not quietly add a test and claim the original plan was
complete.

## Preserve disagreement

If the executor, second model, deterministic check, and human reviewer disagree, the
record should show each claim and source. Confidence is built by resolving the conflict,
not by selecting the most fluent account. Exceptions and residual risk require the named
human authority.

## Acceptance remains named and human

The assurer can recommend acceptance, identify blockers, or describe residual risk. The
final record names the human authority who judged the evidence sufficient and explains
why. This preserves accountability without requiring that person to repeat every
deterministic check manually.

## Takeaway

Use coding agents for speed and breadth. Counterbalance their self-assessment with an
independent view of scope, Git state, deterministic evidence, and risk ownership.

## Canonical CIS sources

- [Independent assurance task type](../specs/independent-assurance-task-type.md)
- [Delivery and assurance](../specs/delivery-and-assurance-spec.md)
- [Execution, assurance, diagnostics, and learning](../specs/execution-assurance-and-learning-spec.md)
- [Provider-neutral agent execution](../specs/features/agent-execution-coordination-feature.md)

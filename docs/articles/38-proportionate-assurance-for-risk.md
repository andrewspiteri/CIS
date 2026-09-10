---
title: "Proportionate Assurance for Different Risk Classes"
type: article
status: Active
series: "Verification and Assurance"
series_order: 5
owner: "Andrew Spiteri"
last_reviewed: "2026-09-10"
review_cadence: on assurance-policy change
summary: "Focused validation for low-risk work and wider independent evidence as consequence and uncertainty increase."
cis:
  stable_id: change-impact-studio:article:proportionate-assurance-risk
---

# Proportionate assurance for different risk classes

Applying the same verification process to every change produces either excessive
ceremony or inadequate evidence.

## Low risk

A bounded documentation or internal correction may need focused deterministic checks
and an inspectable diff.

## Medium risk

A module behavior change may need focused tests, affected project validation, and review
by someone who examines evidence independently of the executor's report.

## High risk

Security, privacy, data migration, public contracts, authority, and release boundaries
may require wider suites, specialist review, rollback evidence, explicit independent
assurance, and recorded residual risk.

The wider evidence can include mutation testing for safety-critical boundaries, normalized
security-scanner results and exceptions, architecture tests, coverage thresholds, CI-run
artifacts, or a different-provider challenge. Applicability comes from the affected risk
and governed profiles rather than a universal “run everything” rule.

## Expand by consequence

Start with the smallest check capable of failing for the right reason. Expand through
affected modules, integrations, and release gates as dependency breadth and consequence
increase.

Risk classification should change the evidence contract, not merely add a label.

## Risk has more than one dimension

Useful assurance planning considers:

- consequence if the change is wrong;
- likelihood of failure or misuse;
- reversibility and recovery time;
- number of repositories and consumers;
- novelty of the technology or pattern;
- quality and freshness of available evidence;
- privilege, data sensitivity, and public exposure; and
- independence of the implementation and review.

A small code diff can be high risk when it changes authorization. A large mechanical
rename can be lower risk when deterministic tooling and broad compilation prove its
boundary.

## Define an evidence ladder

A proportionate sequence might be:

```text
Contract and policy preflight
  → focused changed-behavior tests
  → affected component or project suites
  → cross-repository and compatibility checks
  → security, mutation, architecture, or browser evidence
  → packaging, deployment, rollback, and operational checks
```

Start at the lowest level capable of disproving the claim. Expand when consequence,
uncertainty, dependency breadth, or a failure demands it. A narrow pass never authorizes
a broad conclusion.

## Examples by risk class

A spelling correction in a Draft article may need documentation validation, link checks,
and diff review. A parser behavior change may need focused unit tests, affected module
tests, and representative fixtures. A public authentication contract may need all of
those plus compatibility analysis, security evidence, browser flows, telemetry, rollout,
rollback, and specialist review.

The categories guide evidence; they do not replace judgment. If a supposedly low-risk
change touches a trust boundary unexpectedly, reclassify it rather than following the
original checklist mechanically.

## Independence should scale too

For low-risk work, independence may mean a deterministic validator and a reviewer who
inspects the diff. Higher-risk work may justify a separate assurance task, another human
specialist, or a different-provider advisory challenge.

A second model can broaden attention but cannot certify compliance or accept an exception.
Independence is strongest when it introduces evidence and authority not controlled by the
executor.

## Govern exceptions to the evidence contract

Unavailable high-risk evidence cannot disappear into a comment. Record the missing check,
reason, owner, compensating evidence, consequence, expiry or follow-up, and human risk
decision. Security findings use their governed exception process rather than a general
“accepted” label.

## Learn from assurance cost

If every minor change requires a full release suite, the evidence model may lack focused
tests. If high-risk changes repeatedly skip browser or mutation evidence because it is too
slow, the delivery system has an assurance capability gap. Improve deterministic tooling
and suite design instead of permanently lowering the policy.

## Record why the level was chosen

An assurance class should cite the affected surfaces and evidence, not only a label. “High
risk because the change modifies public authorization, customer data exposure, and a
supported contract” can be reviewed. “High risk because the template says so” cannot.

The record should also state what would change the level: an additive compatibility
strategy, proven isolation, a narrower rollout, or discovery of another consumer. Risk
assessment remains current evidence rather than a permanent property of the component.

## Prefer capability investment to habitual waivers

Repeated exceptions often indicate missing engineering infrastructure. If security
results cannot be normalized, implement the adapter. If browser tests are unreliable,
improve the environment and observability. If a full suite is too slow for focused work,
create smaller contract tests. Proportionate assurance should make strong evidence easier
to obtain, not normalize its absence.

## Takeaway

Match assurance to consequence, uncertainty, and reversibility. Preserve focused speed
for low-risk work while requiring wider independent evidence where failure matters more.

## Canonical CIS sources

- [Delivery and assurance](../specs/delivery-and-assurance-spec.md)
- [Independent assurance task type](../specs/independent-assurance-task-type.md)
- [Final delivery sweep task type](../specs/final-delivery-sweep-task-type.md)
- [Security testing and evidence](../specs/security-testing-and-evidence-spec.md)
- [CI investigation and safe operations](../specs/ci-investigation-and-operations-spec.md)

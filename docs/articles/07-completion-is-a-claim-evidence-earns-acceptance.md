---
title: "Completion Is a Claim; Evidence Earns Acceptance"
type: article
status: Active
series: "Governed Software Change"
series_order: 7
owner: "Andrew Spiteri"
last_reviewed: "2026-09-22"
review_cadence: on verification or assurance change
summary: "Why implementation success and executor reports must be checked against independently observed Git and validation evidence."
cis:
  stable_id: change-impact-studio:article:evidence-earns-acceptance
---

# Completion is a claim; evidence earns acceptance

At the end of an implementation task, an executor usually reports success:

- the feature is complete;
- these files changed;
- the tests pass;
- no unrelated code was touched; and
- the acceptance criteria are satisfied.

That report may be accurate. It is still a claim made by the same actor that performed
the work.

Reliable delivery requires a second view: independently observe the repository, compare
the result with approved scope, validate the required behavior, and let the appropriate
human authority decide whether the evidence is sufficient.

## Execution success is not completion

Several events are commonly mistaken for completion:

- an agent exits successfully;
- a workflow returns code zero;
- a pull request is opened;
- tests pass;
- an issue is closed; or
- the expected files appear in the diff.

Each event is useful evidence. None proves the complete outcome.

Tests may not cover the changed behavior. The plan may have omitted an affected
contract. A required documentation update may be missing. The executor may have changed
an unplanned workflow. A tracker may have been closed manually. The code can be correct
while the intended outcome remains incomplete.

Completion is a governed lifecycle transition, not an interpretation of executor confidence.

## Freeze the expectation before implementation

Independent verification is only possible when the expected change was recorded first.
Before execution, CIS preserves:

- the exact Git or graph baseline;
- the change outcome and acceptance criteria;
- accepted impact findings;
- resolved decisions;
- bounded work items and dependencies;
- expected paths, repositories, and artifacts;
- validation requirements; and
- design, manual-test, and assurance obligations where applicable.

This snapshot prevents the definition of success from being rewritten to match the
implementation after the fact.

The plan may be revised, but revision is explicit and returns affected work to the
appropriate review state. History remains visible.

## Ask Git what actually changed

An executor's changed-file list is useful for orientation, but Git is the independent
source of the actual repository difference.

CIS captures a baseline file snapshot and compares the observed workspace change with
planned repository and path targets. The comparison can classify results such as:

- **Expected and changed:** planned work is visible in the implementation;
- **Expected but missing:** the plan required change that did not occur;
- **Unexpected change:** the implementation touched unplanned scope; and
- **Validation failed:** required evidence did not pass.

Unexpected does not automatically mean wrong. Implementation can reveal a legitimate
missing impact. The finding remains visible until a reviewer decides whether to revise
scope, revert the change, or record a follow-up.

The critical property is that surprise is not normalized away by the executor's narrative.

## Verification is broader than testing

Testing asks whether specified behavior works under the exercised conditions.
Verification asks whether the delivered change matches the complete approved obligation.

Evidence may include:

- focused unit, integration, contract, architecture, or browser tests;
- API compatibility comparison;
- build and packaging results;
- documentation and catalog validation;
- standards and skills validation;
- rendered design artifacts and their source digests;
- migration and rollback evidence;
- security and privacy review;
- operational checks and telemetry expectations;
- actual Git paths and repository boundaries; and
- explicitly unavailable checks and residual risks.

The right evidence depends on the affected risk, not on a fixed test pyramid.
An exit code of zero without the expected parseable result is not a pass. CIS records
missing or malformed required output as `InvalidEvidence`, keeping process success
separate from proof of the engineering claim.

## Run the smallest check that can fail for the right reason

Full test suites are valuable, but they are often a poor first diagnostic. A narrow
contract check can identify the relevant failure faster than a long end-to-end run.

A proportionate assurance ladder is:

```text
Route and contract preflight
        ↓
Changed-behavior checks
        ↓
Affected module or project
        ↓
Wider high-risk suites
        ↓
Packaging, release, or deployment gates
```

Start with the smallest deterministic check that is capable of failing for the intended
reason. Expand as dependency breadth and consequence require. Record every exact command
and outcome.

This approach improves both speed and diagnostic quality without using a narrow pass to
support a broad completion claim.

## Independent assurance is risk-based

Not every change needs a separate assurance team. Independence means the acceptance
decision does not rely solely on the executor's report.

- A low-risk documentation correction may need focused deterministic validation.
- A medium-risk module change may need focused tests and review by somebody who examines
  the actual diff and evidence.
- A high-risk security, data, compatibility, authority, or release change may require a
  separate assurance task, wider suites, specialist review, and recorded residual risk.

A model may help identify suspicious changes or missing checks. Advisory analysis cannot
prove compliance, dismiss a deterministic failure, approve an exception, or accept risk.

## Acceptance belongs to a human authority

CIS validation can establish that required evidence exists and that no structural gate
is failing. It cannot decide that the remaining risk is acceptable.

Final verification acceptance records reviewer identity, rationale, timestamp, and
residual risk. The accepted record is canonical. Supporting logs, caches, workflow runs,
and diagnostic snapshots can remain disposable because the durable evidence summary
points to the exact checks and artifacts.

An accepted change can then move through normal pull-request, merge, release, deployment,
and operational processes. CIS does not replace those systems or infer their authority.

## Worked contrast: “done” versus independently evidenced

The following verification result is hypothetical. The paths illustrate the comparison;
they are not captured Friends Todo repository evidence.

An executor finishes the invitation API task and reports:

> Revocation is implemented. The controller, application service, and tests were updated.
> All focused tests pass, and no unrelated files changed.

That summary helps the reviewer understand the executor's claim. It is not enough to
accept the work.

An independent comparison with the approved baseline produces a different picture:

| Approved expectation or claim | Observed evidence | Consequence |
|---|---|---|
| Revocation behavior changes | Application code and focused lifecycle tests changed | Expected work is present |
| Persistence supports the approved lifecycle | The planned migration is absent | Expected work is missing and requires investigation |
| No delivery changes are required | A deployment workflow changed | Unexpected scope needs disposition |
| Required behavior is verified | Focused tests pass; contract validation passed | Evidence supports only the exercised behavior and contract |
| Customer error behavior remains correct | Required browser evidence was not run | The acceptance boundary is not yet satisfied |

The missing migration does not prove that the implementation is wrong; the chosen
design may not require one, or the plan may be stale. The workflow edit may be necessary.
The unavailable browser evidence may have an acceptable explanation. Governance does
not decide those questions from filenames alone. It prevents them from disappearing
inside a successful executor summary.

The reviewer must reconcile the plan or implementation, dispose the unexpected change,
obtain or explicitly account for required evidence, and record residual risk. Only then
can an authorized human decide whether the completion claim has earned acceptance.

## Takeaway

Treat every completion statement as a claim to be tested against the approved change.

Record expectations before execution. Observe the Git difference independently. Verify
requirements with proportionate evidence. Keep missing, unexpected, failed, and skipped
work visible. Let a human authority accept the result and residual risk.

Execution produces a candidate change. Evidence earns acceptance.

## Canonical CIS sources

- [Delivery and assurance](../specs/delivery-and-assurance-spec.md)
- [Execution, assurance, diagnostics, and learning](../specs/execution-assurance-and-learning-spec.md)
- [Verification task type](../specs/verification-task-type.md)
- [Change impact and bounded planning](../specs/change-impact-and-planning-spec.md)
- [Security testing and evidence](../specs/security-testing-and-evidence-spec.md)

---
title: "The Difference Between Testing, Verification, and Acceptance"
type: article
status: Active
series: "Verification and Assurance"
series_order: 4
owner: "Andrew Spiteri"
last_reviewed: "2026-09-10"
review_cadence: on assurance change
summary: "Tests exercise behavior, verification checks the approved delivery obligation, and acceptance applies human authority to the evidence."
cis:
  stable_id: change-impact-studio:article:testing-verification-acceptance
---

# The difference between testing, verification, and acceptance

These three activities support each other, but they answer different questions.

## Testing

Testing asks whether specified behavior works under exercised conditions. Unit,
integration, contract, browser, architecture, and operational tests provide different
forms of evidence.

A test proves only what it actually asserts. Its name and pass status cannot establish
uncovered behavior.

CIS keeps the intended test suites and their applicability in a governed profile, while
result, coverage, mutation, and diagnostic artifacts remain derived. `cis test reconcile`
parses declared result formats, correlates evidence with the suite and workflow attempt,
and treats missing or unreadable expected artifacts as invalid evidence rather than a pass.

## Verification

Verification asks whether the delivered change satisfies the approved engineering
obligation. It includes tests, but also scope comparison, documentation, contracts,
design artifacts, migrations, security, operations, and missing or unexpected work.

## Acceptance

Acceptance is the human decision that current evidence is sufficient and residual risk
is acceptable. A tool can validate structural readiness; it cannot invent the reviewer
or rationale.

## Why separation matters

If testing implies acceptance, a passing suite can conceal an incomplete plan. If
verification implies acceptance, a structurally valid evidence pack can accept risk
without an owner.

## Follow one claim through all three stages

Suppose the requirement says that a revoked invitation cannot be redeemed and that the
attempt is auditable.

Testing can exercise revocation followed by redemption, assert the response and data
state, and confirm an audit event. Verification checks that the approved API, permission,
data, observability, documentation, and test obligations were all delivered from the
right baseline. Acceptance decides whether those results, unavailable checks, deferrals,
and residual risks are sufficient to release.

One passing integration test contributes evidence at every stage but does not collapse
the stages into one.

## Test design begins before implementation

Stable functional requirements can produce manual-test cases and guide automated suites
before code exists. This makes expected behavior and negative cases part of planning.
Execution results remain separate artifacts so a later pass does not rewrite what the
case was meant to establish.

The governed suite profile records which projects, result formats, coverage thresholds,
mutation boundaries, and environments apply. Discovery can propose suites, but reviewed
profiles decide which evidence the change requires.

## Verification integrates heterogeneous evidence

Not every obligation is a test assertion. Verification may need:

- Git comparison against planned repositories and paths;
- requirement and accepted-impact coverage;
- API or event compatibility results;
- migration and rollback evidence;
- design and accessibility review;
- normalized security findings and exceptions;
- package or installed-tool smoke checks;
- CI-run identity and retained artifacts; and
- explicit unavailable evidence and residual risk.

The verification record connects these sources to the approved task. It does not claim
that one type of evidence substitutes for every other type.

## Acceptance is a decision under uncertainty

Complete evidence does not mean zero risk. A reviewer may accept a known browser-test gap
for a documentation-only correction and reject the same gap for a customer authentication
flow. The rationale should name the evidence, consequence, compensating controls, and
remaining risk.

Automation can prevent acceptance when required structure or mandatory checks are absent.
It cannot decide that the remaining risk is reasonable for the product.

## Keep failures distinct

A failed assertion, skipped suite, unavailable environment, malformed result, stale
artifact, and successful process with no expected evidence are different states. Treating
all as “test failed” loses recovery information; treating all non-red states as pass loses
assurance.

`cis test reconcile` preserves suite and attempt identity so a result cannot be borrowed
from another run merely because the filenames match.

## Avoid three common substitutions

First, do not substitute code coverage for behavior. Coverage can show executed lines and
gaps; it cannot prove meaningful assertions. Second, do not substitute verification
readiness for acceptance. A complete evidence pack still needs an authorized risk
decision. Third, do not substitute acceptance for future correctness. Acceptance records
that the current evidence was sufficient at a point in time, not that the system can
never fail.

Keeping those limits visible makes later incidents easier to investigate. Teams can see
which behavior was tested, which delivery obligations were verified, and which risk was
knowingly accepted.

## Takeaway

Use tests to establish behavior evidence. Use verification to compare the complete
delivery with approved scope. Use human acceptance to decide whether the evidence and
remaining risk are sufficient.

## Canonical CIS sources

- [Testing standard](../standards/testing-standard.md)
- [Delivery and assurance](../specs/delivery-and-assurance-spec.md)
- [Verification task type](../specs/verification-task-type.md)
- [Test suite profile](../references/test-suite-profile.md)
- [`cis test reconcile`](../manual/cis_test_reconcile.md)

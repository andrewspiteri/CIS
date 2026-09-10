---
title: "Deterministic, Manual, and Advisory Enforcement"
type: article
status: Active
series: "Standards and Engineering Policy"
series_order: 5
owner: "Andrew Spiteri"
last_reviewed: "2026-09-10"
review_cadence: on standards-enforcement change
summary: "Different rules need validators, tests, human review, or model advice, and those evidence strengths must remain distinct."
cis:
  stable_id: change-impact-studio:article:enforcement-evidence-types
---

# Deterministic, manual, and advisory enforcement

Not every engineering expectation can be enforced by the same mechanism.

## Deterministic evaluators

Parsers and validators can establish metadata, path, schema, catalog, or contract facts
with stable pass and fail semantics.

## Tests and architecture tests

Behavior tests exercise runtime contracts. Architecture tests enforce structural
boundaries such as prohibited dependencies.

Structured result adapters can reconcile test, coverage, mutation, security, and CI
artifacts with their declared suite, workflow attempt, repository revision, and profile
digest. This is deterministic evidence processing; it does not enlarge what the underlying
check actually proved.

## Manual review

Some rules require contextual judgment: usability, risk, rationale quality, or whether
an exception is acceptable. The evidence needs a named reviewer and reason.

## Advisory models

Models can identify likely duplicates, conflicts, or suspicious patterns. Their output
remains a candidate until confirmed by deterministic evidence or explicit human review.

## Not mapped

The absence of an evaluator is a visible gap, not proof of compliance and not an
automatic exception.

## Choose evidence for the claim

An evidence mechanism is strong only for the question it can answer. A parser is excellent
at validating a required field and poor at judging whether a rationale is convincing. A
human can assess context and may overlook a structural omission that a validator finds
immediately. A model can broaden attention and cannot grant approval.

Enforcement design should begin with the rule's observable claim, then select the
strongest appropriate combination.

## Combine methods without flattening them

Consider a rule requiring public handlers to avoid direct persistence. Static or compiler
analysis can reject prohibited dependencies in supported code. Integration tests can
exercise cache hit and miss behavior. Manual review can assess dynamic provider routes and
failure policy. A model might flag suspicious unrecognized patterns.

The final record keeps each result distinct. A manual pass does not convert the advisory
finding into deterministic evidence, and a clean static scan does not prove the runtime
cache policy.

## Result adapters need contracts

Test, coverage, mutation, security, and CI tools produce different formats and exit
semantics. A CIS adapter binds the declared suite, workflow attempt, repository revision,
profile digest, artifact, and parse outcome. Missing or malformed expected evidence is
invalid even if the wrapper exits successfully.

Normalization supports comparison; it does not enlarge coverage. If a scanner excludes a
language or path, the reconciled result must preserve that boundary.

## Manual review should be structured

“Reviewed manually” is not enough. Record the rule, reviewer, source and revision,
questions considered, findings, rationale, and date. Where appropriate, provide a
repeatable checklist without pretending that the checklist removes judgment.

Manual evidence can be the correct final mechanism for usability, architectural rationale,
or residual-risk decisions. It should not be treated as a temporary embarrassment simply
because it is not automated.

## Advisory analysis routes attention

Models and heuristics can identify possible conflicts, duplicate standards, missing tests,
or suspicious changes. Their best role is to prioritize inspection. Confirmation comes
from opening the canonical source, running a deterministic check, or obtaining explicit
human review.

## Treat unmapped rules as visible debt

An unmapped rule still applies. The conformance report should show that no evaluator is
defined and planning should select appropriate manual evidence until the gap is improved.
Creating a fake automated pass would be worse than an honest manual route.

Prioritize gaps by consequence, frequency, and the feasibility of exact enforcement.

## Evolve routes without changing history

When a manual rule gains an automated evaluator, update the conformance mapping and test
the evaluator against representative passing and failing fixtures. Historical manual
evidence remains manual; it is not retrospectively upgraded. During transition, both
routes may be required until the automation's coverage and false-positive behavior are
understood.

## Takeaway

Match each rule to the strongest appropriate evidence. Preserve the distinction between
deterministic result, test evidence, human judgment, model advice, and enforcement gaps.

## Canonical CIS sources

- [Standards governance](../specs/standards-governance-spec.md)
- [Documentation governance standard](../standards/documentation-governance-standard.md)
- [Standards conformance matrix](../references/standards-conformance-matrix.md)

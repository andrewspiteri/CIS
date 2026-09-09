---
title: "Conformance Is Not the Same as Compliance"
type: article
status: Draft
series: "Standards and Engineering Policy"
series_order: 4
owner: "Andrew Spiteri"
last_reviewed: "2026-09-09"
review_cadence: on standards-conformance change
summary: "A conformance matrix describes enforcement routes and gaps; it does not certify every implementation or regulatory obligation."
cis:
  stable_id: change-impact-studio:article:conformance-not-compliance
---

# Conformance is not the same as compliance

The word compliance often implies a broad legal, regulatory, or organizational verdict.
CIS standards conformance has a narrower purpose: show how each repository rule is
expected to be evaluated.

## The matrix maps routes

For every Active rule, the conformance matrix records the enforcement kind, evaluator
or evidence location, lifecycle, and notes. It can expose rules that are manual,
advisory-only, or not mapped.

## A mapping is not a result

Knowing that an architecture test enforces a rule does not prove the current change
passed that test. Conformance commands show the declared route; separate evaluation and
verification produce results.

## Repository rules are not universal certification

A green repository check cannot establish external regulatory compliance unless the
full authority, scope, evidence, and assessor requirements are explicitly represented.

## Gaps are useful

`not-mapped` is honest governance state. It identifies an enforcement opportunity
without pretending the rule is automatically satisfied or creating an exception.

## Separate four different claims

| Claim | What it means |
|---|---|
| Rule applicability | The obligation governs this repository or change |
| Enforcement mapping | A declared evaluator or review route exists for the rule |
| Conformance result | Current evidence satisfied that route within its scope |
| Compliance conclusion | An authorized assessment covered the complete external or organizational obligation |

Each step can support the next, but none is automatically equivalent. A rule may apply
without an evaluator. An evaluator may exist without having run. A passing repository
rule may be only one control in a broader regulatory assessment.

## Read a conformance row precisely

A useful matrix row names the stable rule, enforcement kind, evaluator or evidence
location, lifecycle, applicability, and notes. For a public persistence-isolation rule,
the route might include an architecture test and manual review of dynamic paths.

The matrix shows how the repository intends to evaluate the rule. Verification must still
record the exact test result and review. If the architecture test covers only C# handlers,
the matrix should not imply that Node.js or generated provider routes were assessed.

## Green results have boundaries

A passing standards validator can prove that Active rules have unique IDs and valid
mappings. It cannot prove that every mapped test ran against the candidate change. A
successful test can prove its assertions under the exercised configuration. It cannot
prove there is no uncovered implementation path.

Good reporting names the scope: “repository standard CIS-STD-API-004 conformed under
evaluator X for revision Y.” Avoid labels such as “fully compliant” unless the broader
authority and assessment genuinely support them.

## Gaps create an engineering backlog

`not-mapped`, advisory-only, and manual routes show where assurance depends on human
effort or is absent. Teams can prioritize deterministic evaluators for high-risk,
frequently applied rules. The gap is neither a failure of the implementation nor an
exception to the rule; it is a limitation in the evidence system.

## External compliance remains externally scoped

Legal, regulatory, contractual, and organizational frameworks have their own applicability,
control owners, evidence periods, environments, sampling, and assessor requirements. CIS
can store related standards and evidence links, but it must not claim certification from a
repository mapping unless that complete authority is represented and reviewed.

## Use precise language in release and audit records

State which rules applied, which evaluators ran, which passed or failed, which evidence
was unavailable, which exceptions remain, and who accepted residual risk. Precision makes
the evidence useful to a compliance process without impersonating that process.

## Ask what would falsify the claim

For every green conformance statement, identify the evidence that could make it fail and
the scope it did not inspect. If no falsifiable evaluator or named reviewer exists, the
row is a mapping or assertion rather than a result. This habit keeps dashboards, release
notes, and audit exports from gradually inflating narrow repository evidence into a
universal assurance claim.

## Takeaway

Use conformance to make rule enforcement visible and traceable. Do not turn a repository
mapping or validator pass into a broader compliance claim it was not designed to support.

## Canonical CIS sources

- [Standards governance](../specs/standards-governance-spec.md)
- [Standards conformance matrix](../references/standards-conformance-matrix.md)
- [`cis standards conformance`](../manual/cis_standards_conformance.md)

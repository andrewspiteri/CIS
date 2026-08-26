---
title: "Conformance Is Not the Same as Compliance"
type: article
status: Draft
series: "Standards and Engineering Policy"
series_order: 4
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
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

## Takeaway

Use conformance to make rule enforcement visible and traceable. Do not turn a repository
mapping or validator pass into a broader compliance claim it was not designed to support.

## Canonical CIS sources

- [Standards governance](../specs/standards-governance-spec.md)
- [Standards conformance matrix](../references/standards-conformance-matrix.md)
- [`cis standards conformance`](../manual/cis_standards_conformance.md)


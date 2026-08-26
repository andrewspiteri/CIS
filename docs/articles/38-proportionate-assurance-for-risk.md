---
title: "Proportionate Assurance for Different Risk Classes"
type: article
status: Draft
series: "Verification and Assurance"
series_order: 5
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
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

## Expand by consequence

Start with the smallest check capable of failing for the right reason. Expand through
affected modules, integrations, and release gates as dependency breadth and consequence
increase.

Risk classification should change the evidence contract, not merely add a label.

## Takeaway

Match assurance to consequence, uncertainty, and reversibility. Preserve focused speed
for low-risk work while requiring wider independent evidence where failure matters more.

## Canonical CIS sources

- [Delivery and assurance](../specs/delivery-and-assurance-spec.md)
- [Independent assurance task type](../specs/independent-assurance-task-type.md)
- [Final delivery sweep task type](../specs/final-delivery-sweep-task-type.md)


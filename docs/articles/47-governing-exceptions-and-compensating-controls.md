---
title: "Governing Exceptions and Compensating Controls"
type: article
status: Draft
series: "Standards and Engineering Policy"
series_order: 6
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: on exception-policy change
summary: "An exception needs exact rule identity, human authority, bounded scope, review conditions, and alternative risk controls."
cis:
  stable_id: change-impact-studio:article:exceptions-compensating-controls
---

# Governing exceptions and compensating controls

Real systems sometimes cannot satisfy a standard exactly. Hiding the deviation weakens
governance; banning every deviation pushes decisions outside the record.

## Name the exact rule

An exception cites a stable rule ID rather than suspending an entire standard.

## Record authority and rationale

A named human approver explains why the exception is acceptable. An agent cannot approve
an exception to the rules governing its own work.

## Bound the scope

The record identifies repositories, components, change, environments, and duration. An
exception for one legacy endpoint cannot silently become product-wide policy.

## Require review or expiry

Temporary constraints need an expiry date or review condition. Durable deviations may
indicate that the standard itself needs revision.

## Preserve compensating controls

Alternative monitoring, isolation, validation, or operational procedures reduce risk
while the primary rule is unmet. Their evidence belongs in the change and verification record.

## Takeaway

Exceptions are governed decisions, not validator switches. Make them exact, human-owned,
bounded, reviewable, and supported by explicit compensating controls.

## Canonical CIS sources

- [Documentation governance standard](../standards/documentation-governance-standard.md)
- [Standards governance](../specs/standards-governance-spec.md)
- [Delivery and assurance](../specs/delivery-and-assurance-spec.md)


---
title: "Stable Rule IDs and Why They Matter"
type: article
status: Draft
series: "Standards and Engineering Policy"
series_order: 2
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: on standard-rule change
summary: "Immutable rule identities connect expectations to plans, evaluators, evidence, exceptions, and history."
cis:
  stable_id: change-impact-studio:article:stable-rule-ids
---

# Stable rule IDs and why they matter

Section numbers and bullet positions change. A rule needs an identity that survives
editing.

## Trace exact obligations

`CIS-STD-DOC-007` identifies one exception rule more precisely than “the exceptions
section.” Plans, findings, checks, and approvals can cite it without copying the text.

## Preserve history

Retired IDs are never reused. Historical evidence remains attached to the obligation
that existed at the time, even after the standard evolves.

## Map enforcement

The conformance matrix uses document and rule IDs to associate evaluators, architecture
tests, manual reviews, and advisory evidence. Stronger enforcement can replace a gap
without changing the rule's identity.

## Scope exceptions

An exception names the exact rule, approver, bounded scope, rationale, expiry or review
condition, and compensating controls. It does not suspend an entire document vaguely.

## Takeaway

Stable rule IDs make standards executable as engineering references. Treat them as
immutable contracts and preserve retired identities for auditability.

## Canonical CIS sources

- [Documentation governance standard](../standards/documentation-governance-standard.md)
- [Standards governance](../specs/standards-governance-spec.md)
- [Standards conformance matrix](../references/standards-conformance-matrix.md)


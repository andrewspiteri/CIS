---
title: "Business Invariant Catalogue Specification"
type: specification
status: Draft
owner: Repository maintainer
review_cadence: on change
cis:
  stable_id: change-impact-studio:spec:business-invariant-catalogue
---

# Business Invariant Catalogue Specification

## Purpose

This specification governs durable rules, rationale, enforcement points, tests, and source specifications.

## Source of truth

The paired `change-impact-studio:reference:business-invariant-catalogue` reference is the current repository inventory. This specification owns its maintenance rules.

## Required fields

- Invariant ID
- Rule
- Applies to
- Rationale
- Enforcement point
- Related commands / events
- Related tests
- Source specs
- Status

## Maintenance rules

- Use stable, append-only row identities.
- Use row lifecycle values `Draft`, `Planned`, `Verified`, `Deprecated`, or `Withdrawn`.
- Keep deterministic discoveries `Draft` until a maintainer verifies their meaning and relationships.
- Record ownership, evidence, provenance, compatibility, and sensitivity where applicable.
- Update the reference in the same change that changes the governed behavior.
- Retain deprecated rows until the compatibility and replacement story is documented.
- Validate deterministic implementation evidence where available.

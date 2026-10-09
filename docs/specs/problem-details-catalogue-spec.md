---
title: "Problem Details Catalogue Specification"
type: specification
status: Draft
owner: Repository maintainer
review_cadence: on change
cis:
  stable_id: change-impact-studio:spec:problem-details-catalogue
---

# Problem Details Catalogue Specification

## Purpose

This specification governs API error identities, status codes, contracts, and handling.

## Source of truth

The paired `change-impact-studio:reference:problem-details-catalogue` reference is the current repository inventory. This specification owns its maintenance rules.

## Required fields

- Problem ID
- Problem type / reason
- HTTP status
- Raised by
- Meaning
- Frontend handling
- Sensitive fields
- Related commands / workflows
- Status
- Evidence

## Maintenance rules

- Use stable, append-only row identities.
- Use row lifecycle values `Draft`, `Planned`, `Verified`, `Deprecated`, or `Withdrawn`.
- Keep deterministic discoveries `Draft` until a maintainer verifies their meaning and relationships.
- Record ownership, evidence, provenance, compatibility, and sensitivity where applicable.
- Update the reference in the same change that changes the governed behavior.
- Retain deprecated rows until the compatibility and replacement story is documented.
- Validate deterministic implementation evidence where available.

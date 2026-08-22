---
title: "Package Catalogue Specification"
type: specification
status: Draft
owner: Repository maintainer
review_cadence: on change
cis:
  stable_id: change-impact-studio:spec:package-catalogue
---

# Package Catalogue Specification

## Purpose

This specification governs runtime and build dependencies, ownership, purpose, and update policy.

## Source of truth

The paired `change-impact-studio:reference:package-catalogue` reference is the current repository inventory. This specification owns its maintenance rules.

## Required fields

- Package
- Component
- Purpose
- Version policy
- Dependency class
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

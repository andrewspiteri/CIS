---
title: "Permissions Dictionary Specification"
type: specification
status: Draft
owner: Repository maintainer
review_cadence: on change
cis:
  stable_id: change-impact-studio:spec:permissions-dictionary
---

# Permissions Dictionary Specification

## Purpose

This specification governs permissions, roles, enforcement points, and capability mappings.

## Source of truth

The paired `change-impact-studio:reference:permissions-dictionary` reference is the current repository inventory. This specification owns its maintenance rules.

## Required fields

- Permission code
- Permission name
- Domain
- Capability group
- Action
- Scope type
- Applies to
- Default role mappings
- Customer assignable
- Endpoint / API usage
- Frontend capability
- Source location
- Status
- Notes

## Maintenance rules

- Use stable, append-only row identities.
- Use row lifecycle values `Draft`, `Planned`, `Verified`, `Deprecated`, or `Withdrawn`.
- Keep deterministic discoveries `Draft` until a maintainer verifies their meaning and relationships.
- Record ownership, evidence, provenance, compatibility, and sensitivity where applicable.
- Update the reference in the same change that changes the governed behavior.
- Retain deprecated rows until the compatibility and replacement story is documented.
- Validate deterministic implementation evidence where available.

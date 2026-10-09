---
title: "API Dictionary Specification"
type: specification
status: Draft
owner: Repository maintainer
review_cadence: on change
cis:
  stable_id: change-impact-studio:spec:api-dictionary
---

# API Dictionary Specification

## Purpose

This specification governs API contracts, operations, routes, ownership, and lifecycle.

## Source of truth

The paired `change-impact-studio:reference:api-dictionary` reference is the current repository inventory. This specification owns its maintenance rules.

## Required fields

- API ID
- Version
- Method
- Path
- Module
- Host
- Exposure
- Consumer
- Request contract
- Response contract
- Permission / auth
- OpenAPI operation
- Trusted scope source
- Rate-limit policy
- Idempotency / concurrency
- Collection semantics
- Error contract
- Cache policy
- Data access path
- Status
- Evidence
- Known tests
- Notes

## Maintenance rules

- Use stable, append-only row identities.
- Use row lifecycle values `Draft`, `Planned`, `Verified`, `Deprecated`, or `Withdrawn`.
- Keep deterministic discoveries `Draft` until a maintainer verifies their meaning and relationships.
- Record ownership, evidence, provenance, compatibility, and sensitivity where applicable.
- Update the reference in the same change that changes the governed behavior.
- Retain deprecated rows until the compatibility and replacement story is documented.
- Validate deterministic implementation evidence where available.

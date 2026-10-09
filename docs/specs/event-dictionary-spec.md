---
title: "Event Dictionary Specification"
type: specification
status: Draft
owner: Repository maintainer
review_cadence: on change
cis:
  stable_id: change-impact-studio:spec:event-dictionary
---

# Event Dictionary Specification

## Purpose

This specification governs published and consumed event contracts, ownership, and compatibility.

## Source of truth

The paired `change-impact-studio:reference:event-dictionary` reference is the current repository inventory. This specification owns its maintenance rules.

## Required fields

- Event ID
- Event name
- Event type
- Producer
- Consumers
- Triggering command
- Payload summary
- Ordering / idempotency
- Sensitivity
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

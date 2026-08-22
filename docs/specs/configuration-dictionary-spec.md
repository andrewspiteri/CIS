---
title: "Configuration Dictionary Specification"
type: specification
status: Draft
owner: Repository maintainer
review_cadence: on change
cis:
  stable_id: change-impact-studio:spec:configuration-dictionary
---

# Configuration Dictionary Specification

## Purpose

This specification governs runtime configuration keys, sources, sensitivity, and refresh behavior.

## Source of truth

The paired `change-impact-studio:reference:configuration-dictionary` reference is the current repository inventory. This specification owns its maintenance rules.

## Required fields

- Name
- Path
- Allowed values
- Refresh class
- Owner
- Sensitive
- Description
- Status
- Evidence

## Maintenance rules

- Use stable, append-only row identities.
- Treat `Owner + Path` as the stable row identity. Environment-specific sources and allowed values for the same owned key are consolidated into that row.
- Use row lifecycle values `Draft`, `Planned`, `Verified`, `Deprecated`, or `Withdrawn`.
- Keep deterministic discoveries `Draft` until a maintainer verifies their meaning and relationships.
- Record ownership, evidence, provenance, compatibility, and sensitivity where applicable.
- Update the reference in the same change that changes the governed behavior.
- Retain deprecated rows until the compatibility and replacement story is documented.
- Validate deterministic implementation evidence where available.

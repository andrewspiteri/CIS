---
title: "Projection Dictionary Specification"
type: specification
status: Draft
owner: Repository maintainer
review_cadence: on change
cis:
  stable_id: change-impact-studio:spec:projection-dictionary
---

# Projection Dictionary Specification

## Purpose

This specification governs read models and projections, their sources, consumers, freshness, security, and rebuild behavior.

## Source of truth

The paired `change-impact-studio:reference:projection-dictionary` reference is the current repository inventory. This specification owns its maintenance rules.

## Required fields

- Projection ID
- Projection / read model
- Source of truth
- Producer / refresh
- Consumers
- Fields summary
- Freshness / staleness
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

---
title: "Workflow State Dictionary Specification"
type: specification
status: Draft
owner: Repository maintainer
review_cadence: on change
cis:
  stable_id: change-impact-studio:spec:workflow-state-dictionary
---

# Workflow State Dictionary Specification

## Purpose

This specification governs workflow states, meanings, transitions, blockers, terminal states, and visibility behavior.

## Source of truth

The paired `change-impact-studio:reference:workflow-state-dictionary` reference is the current repository inventory. This specification owns its maintenance rules.

## Required fields

- Workflow ID
- Workflow
- State
- Meaning
- Allowed transitions
- Triggering commands
- Blocking rules
- Terminal
- Visibility / search behavior
- Status

## Maintenance rules

- Use stable, append-only row identities.
- Use row lifecycle values `Draft`, `Planned`, `Verified`, `Deprecated`, or `Withdrawn`.
- Keep deterministic discoveries `Draft` until a maintainer verifies their meaning and relationships.
- Record ownership, evidence, provenance, compatibility, and sensitivity where applicable.
- Update the reference in the same change that changes the governed behavior.
- Retain deprecated rows until the compatibility and replacement story is documented.
- Validate deterministic implementation evidence where available.

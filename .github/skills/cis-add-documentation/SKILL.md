---
name: cis-add-documentation
description: Create and register repository documentation with correct placement, metadata, authority, navigation, and validation.
---

# CIS Add Documentation

## Purpose

Add documentation without creating orphaned files or competing sources of truth.

## When to Use

Use when creating a policy, standard, specification, workflow, procedure, ADR, reference, template, guide, checklist, or change record.

## Inputs

Gather title, document type, scope, audience, owner, authority, lifecycle, related documents, and whether the item is normative.

## Workflow

1. Classify and place the item under `docs/`.
2. Add front matter with a stable ID and truthful lifecycle state.
3. Register it in `docs/catalog.yml` and update nearby navigation.
4. Link related authorities and avoid duplicated source-of-truth content.
5. Run `cis docs validate --strict`.

## Output Expectations

Leave a correctly placed, cataloged, navigable, and validated document.

## Guardrails

Do not invent top-level roots casually, misuse lifecycle status, or create a second canonical source for the same contract.

## Related Files

See `docs/README.md`, `docs/catalog.yml`, and `docs/templates/`.

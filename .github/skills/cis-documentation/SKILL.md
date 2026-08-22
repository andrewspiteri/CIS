---
name: cis-documentation
description: Maintain CIS specifications, references, templates, and catalog metadata when repository behavior or documentation authority changes.
---

# CIS Documentation Maintenance

## Purpose

Keep repository-owned documentation navigable, authoritative, and synchronized with behavior.

## When to Use

Use when adding or changing a specification, reference, ADR, template, guide, policy, standard, procedure, or implementation contract.

## Inputs

Read `.cis/repository.yml`, `docs/README.md`, `docs/catalog.yml`, and related canonical documents.

## Workflow

1. Classify the document and confirm its scope, authority, owner, lifecycle, and source-of-truth relationship.
2. Preserve stable IDs and update the nearest navigation and catalog entry.
3. Update implementation and documentation together when behavior changes.
4. Distinguish discovered, proposed, verified, deprecated, and withdrawn facts.
5. Run `cis docs validate --strict` before completion.

## Output Expectations

Leave valid front matter, catalog registration, navigation, cross-links, and explicit review status.

## Guardrails

Do not create duplicate authorities, treat generated summaries as canonical, or silently replace human-authored content.

## Related Files

See `docs/catalog.yml` and `.github/skills/cis-add-documentation/SKILL.md`.

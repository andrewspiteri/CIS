---
name: cis-maintain-domain-behaviour
description: Keep commands, events, workflow states, invariants, projections, problem details, and module boundaries aligned with behavior changes.
---

# CIS Maintain Domain Behaviour

## Purpose

Preserve a reviewable model of durable system behavior across implementation and documentation.

## When to Use

Use when a change adds, removes, renames, or alters commands, events, states, transitions, rules, read models, errors, or ownership boundaries.

## Inputs

Gather feature and technical specifications, affected code and tests, and the domain-behavior references under `docs/references/`.

## Workflow

1. Classify every changed behavior surface.
2. Update matching reference rows with stable IDs, source evidence, lifecycle, and relationships.
3. Keep command/event identities distinct and connect transitions, invariants, projections, and problems.
4. Update module boundaries and contract references where the same change crosses them.
5. Validate documentation and implementation evidence before handoff.

## Output Expectations

Leave aligned behavior references, related contract updates, and explicit validation evidence.

## Guardrails

Do not mark discovered behavior verified without evidence or make a generated summary authoritative.

## Related Files

Use `docs/specs/`, `docs/references/`, and the repository profile.

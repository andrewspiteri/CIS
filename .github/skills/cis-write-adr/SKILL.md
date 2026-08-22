---
name: cis-write-adr
description: Draft or update an architecture decision record for a durable technical choice with alternatives, consequences, and affected contracts.
---

# CIS Write ADR

## Purpose

Preserve why a durable technical choice was made and when it should be revisited.

## When to Use

Use for long-lived architecture, boundary, data, security, integration, deployment, or operational decisions.

## Inputs

Gather decision context, constraints, alternatives, evidence, affected specifications/references, stakeholders, and revisit conditions.

## Workflow

1. Copy `docs/templates/adr-template.md` into `architecture/decisions/`.
2. Assign a stable ID and Proposed status.
3. Record context, decision, alternatives, consequences, risks, and affected contracts.
4. Update technical intent and related specifications after acceptance.
5. Register and validate the ADR.

## Output Expectations

Leave a reviewable ADR with explicit status, tradeoffs, relationships, and verification or revisit triggers.

## Guardrails

Do not use an ADR for temporary implementation detail or hide rejected alternatives.

## Related Files

See `docs/templates/adr-template.md` and `docs/specs/technical-intent-spec.md`.

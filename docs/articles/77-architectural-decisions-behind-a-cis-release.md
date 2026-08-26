---
title: "Architectural Decisions Behind a CIS Release"
type: article
status: Draft
series: "Product Evolution and Learning"
series_order: 7
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: on release or ADR change
summary: "Use change-local decisions and promoted ADRs to explain why a release changed architecture, not only what files moved."
cis:
  stable_id: change-impact-studio:article:decisions-behind-release
---

# Architectural decisions behind a CIS release

Release notes explain outcomes. Architecture readers also need the choices that shaped
those outcomes.

## Begin in the change

Material questions are recorded with options, evidence, gates, and rationale in the
change dossier before implementation.

## Promote durable decisions

A decision that changes long-term module, storage, provider, compatibility, or trust
boundaries can be promoted to a catalogued ADR. The ADR retains alternatives and the
originating change.

## Link the release

Release communication can point to the ADR, affected technical intent, migration, and
verification evidence. Readers understand both consequence and reasoning.

## Preserve supersession

If a later release changes direction, a new decision supersedes the earlier one. History
shows when and why the architecture moved.

## Takeaway

Use releases to communicate durable decisions, not recreate them retrospectively.
Capture options during planning, promote reviewed choices, and link them to outcomes.

## Canonical CIS sources

- [Change impact and bounded planning](../specs/change-impact-and-planning-spec.md)
- [Architecture decisions index](../architecture/decisions/README.md)
- [`cis decision promote`](../manual/cis_decision_promote.md)


---
title: "Testing an Engineering Governance Tool"
type: article
status: Draft
series: "Building Change Impact Studio"
series_order: 9
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: on test strategy change
summary: "Verification must cover rules, safe mutations, lifecycle, idempotency, integration boundaries, and packaged behavior—not only individual methods."
cis:
  stable_id: change-impact-studio:article:testing-governance-tool
---

# Testing an engineering governance tool

CIS does more than transform input into output. It preserves authority boundaries,
rejects unsafe state, and coordinates lifecycle across modules. Tests must cover those
properties.

## Focused module tests

Each capability tests parsing, validation, identity, output, exit codes, idempotency,
and edge cases within its ownership boundary.

## Host and architecture tests

Composition tests verify explicit registration, duplicate protection, command dispatch,
and public contract boundaries.

## Cross-module delivery tests

Lifecycle tests exercise dossier creation, impact, decisions, planning, design,
execution preparation, verification, and authority gates together.

## Filesystem safety tests

Temporary repositories verify path containment, collisions, dry runs, concurrent edits,
ownership, quarantine, and atomic failure behavior.

## Representative repositories

Golden paths expose integration gaps that fixtures miss: framework-generated files,
dynamic routes, multi-repository ownership, package tooling, and real build behavior.

## Package smoke tests

The installed .NET tool must register required modules and run outside the source build
layout. The editor package needs syntax, test, and packaging checks.

## Takeaway

Test the governance claims: stable authority, safe mutation, honest evidence, lifecycle
gates, and packaged behavior. A unit-green implementation can still violate the product's
control model.

## Canonical CIS sources

- [Delivery and assurance](../specs/delivery-and-assurance-spec.md)
- [Testing standard](../standards/testing-standard.md)
- [Implementation roadmap](../specs/implementation-roadmap.md)


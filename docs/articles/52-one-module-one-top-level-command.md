---
title: "One Module, One Top-Level Command"
type: article
status: Draft
series: "Building Change Impact Studio"
series_order: 2
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: on module architecture change
summary: "A simple ownership rule aligns CLI vocabulary, code boundaries, tests, and future extension contracts."
cis:
  stable_id: change-impact-studio:article:one-module-one-command
---

# One module, one top-level command

CIS gives each module one top-level command: `graph`, `plan`, `verify`, `standards`, and
so on. This is more than CLI organization.

## The command names the capability owner

When behavior belongs to `cis graph`, the Graph module owns construction, validation,
queries, and export. Other modules consume its public contracts rather than duplicating
graph behavior.

## Registration is explicit

Built-in assemblies are loaded deliberately. Duplicate command names fail composition
instead of depending on discovery order.

## Tests follow the same boundary

Focused module tests validate behavior, while host tests verify registration and
dispatch. Cross-module delivery tests cover lifecycle integration.

## Modules are capability-sized

A module should own a coherent domain, not merely provide a folder per command. Too many
tiny modules make composition noisy; one giant module hides ownership.

## Takeaway

One module per top-level command creates a visible contract between product vocabulary,
implementation ownership, composition, and testing.

## Canonical CIS sources

- [Module catalogue](../specs/module-catalog-spec.md)
- [Technical intent](../specs/technical-intent-spec.md)
- [`cis host modules`](../manual/cis_host_modules.md)


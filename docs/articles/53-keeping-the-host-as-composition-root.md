---
title: "Keeping the Host as the Composition Root"
type: article
status: Draft
series: "Building Change Impact Studio"
series_order: 3
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: on composition change
summary: "Cis.Host wires modules, output, and cross-cutting infrastructure while product behavior remains in capability owners."
cis:
  stable_id: change-impact-studio:article:host-composition-root
---

# Keeping the host as the composition root

Dependency injection becomes less useful when every module can construct the application
or discover arbitrary implementations. CIS keeps one composition root in `Cis.Host`.

## The host owns wiring

The host registers built-in modules and separately packaged providers, creates the
service container, constructs the command tree, and applies host-wide output, exit-code,
and usage behavior.

## Abstractions own public contracts

`Cis.Abstractions` contains the contracts modules share. It avoids depending on product
modules and remains small enough to be a stable integration surface.

## Modules own behavior

The host does not implement impact analysis, planning, graph extraction, or verification.
Putting domain logic in composition would make alternative clients and focused tests
depend on host internals.

## Explicit loading protects the trust boundary

CIS never scans a target repository for assemblies to execute as product modules. The
host loads known built-ins and configured provider packages explicitly.

## Takeaway

Keep object construction and cross-cutting wiring in one host. Keep public contracts in
abstractions and domain behavior in capability modules. The boundary improves safety,
testability, and extension clarity.

## Canonical CIS sources

- [Technical intent](../specs/technical-intent-spec.md)
- [System context](../specs/system-context-spec.md)
- [Module catalogue](../specs/module-catalog-spec.md)


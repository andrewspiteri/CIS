---
title: "One Module, One Top-Level Command"
type: article
status: Active
series: "Building Change Impact Studio"
series_order: 2
owner: "Andrew Spiteri"
last_reviewed: "2026-09-10"
review_cadence: on module architecture change
summary: "A simple ownership rule aligns CLI vocabulary, code boundaries, tests, and future extension contracts."
cis:
  stable_id: change-impact-studio:article:one-module-one-command
---

# One module, one top-level command

CIS gives each module one top-level command: `graph`, `plan`, `verify`, `standards`, and
so on. This is more than CLI organization.

Newer capabilities follow the same rule: `definition` coordinates the product-definition
journey, `references` governs non-API dictionaries, `frontend` supplies normalized client
context, `ci` investigates remote runs, `artifacts` manages derived retention, and `mcp`
serves the fixed-repository adapter.

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

## Vocabulary becomes architecture

When users say “build the graph,” `cis graph` identifies the product capability and the
code that owns it. Documentation, manuals, tests, diagnostics, and source structure can
use the same vocabulary. A command that seems to belong equally to several modules often
reveals an unresolved product boundary.

For example, `cis plan derive` may consume graph and feature contracts, but Planning owns
the transaction because the result is approved work structure. Graph supplies public
queries; it does not write plan files. This keeps orchestration with the capability that
owns the user outcome.

## Public contracts point inward

Modules share types and services through `Cis.Abstractions`. They should not reference
another module's internal implementation or use its files as an undocumented API. That
dependency direction makes focused testing possible and prevents a change in storage from
spreading across unrelated capabilities.

Cross-module workflows are still expected. Product definition coordinates BRD, technical
intent, solution design, dictionaries, UI direction, and backlog through their public
contracts while preserving each authority.

## Registration fails early

The host knows which built-in assemblies to load. If two modules claim the same top-level
command, startup fails with a composition error. Deterministic registration avoids
machine-dependent behavior caused by directory scanning or assembly order.

The module inventory command makes the composed product visible to users, packages, and
smoke tests. A missing command in the installed tool is a release failure even when its
project built successfully.

## Choose module size by cohesion

A module is too broad when unrelated commands share no domain rules and changes require
touching a central switch. It is too narrow when most behavior consists of forwarding to
another module and every feature needs many new assemblies.

Good boundaries combine a user-facing capability, its validation and storage rules, and
one top-level vocabulary. Subcommands can remain numerous when they operate on the same
authority and lifecycle.

## Test both ownership and collaboration

Focused tests prove the module's parsing, behavior, output, and failure semantics. Host
tests prove registration and dispatch. Cross-module lifecycle tests prove that public
contracts compose correctly without moving domain logic into the host.

## Avoid utility modules with hidden authority

A shared helper may normalize paths or hashes; it should not decide graph lifecycle,
planning approval, or provider permission. When a utility begins interpreting product
meaning, move that behavior to the owning capability and expose a deliberate contract.

Likewise, avoid a generic `admin` or `tools` command that accumulates unrelated behavior.
Its vocabulary gives users no clue which authority or tests govern an operation.

## Evolve the command surface compatibly

Renaming or moving a top-level command affects manuals, scripts, agent tasks, MCP, the
extension, and release compatibility. Treat the vocabulary as a product contract with a
deprecation and migration path, not an internal refactor.

## Takeaway

One module per top-level command creates a visible contract between product vocabulary,
implementation ownership, composition, and testing.

## Canonical CIS sources

- [Module catalogue](../specs/module-catalog-spec.md)
- [Technical intent](../specs/technical-intent-spec.md)
- [`cis host modules`](../manual/cis_host_modules.md)

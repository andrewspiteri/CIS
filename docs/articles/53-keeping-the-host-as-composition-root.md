---
title: "Keeping the Host as the Composition Root"
type: article
status: Draft
series: "Building Change Impact Studio"
series_order: 3
owner: "Andrew Spiteri"
last_reviewed: "2026-09-09"
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

Provider contracts now cover AI, agent execution, trackers, CI, reference extraction,
frontend context, test/security result adapters, deterministic renderers, and graph
augmentation. Their composition remains explicit even when their capabilities differ.

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

## Composition is a privileged boundary

The composition root decides which executable code joins the process. That makes it part
of the trust model, not just dependency-injection housekeeping. A target repository may
contain build outputs, test plugins, or arbitrary assemblies; none becomes a CIS module
because it is discoverable on disk.

Known built-ins and configured provider packages are loaded through explicit routes. The
host can validate duplicates, versions, dependencies, and capabilities before exposing a
command.

## Keep dependency direction simple

```text
Cis.Host
  → Cis.Abstractions
  → capability modules and configured providers

capability modules
  → Cis.Abstractions

Cis.Abstractions
  → no product module
```

The diagram describes compile-time direction, not every runtime call. Modules can consume
other capabilities through public abstractions, while the host provides concrete
implementations. Abstractions remain free of module-specific storage and orchestration.

## Cross-cutting concerns belong in the host selectively

Output format selection, common result envelopes, standard-error routing, process
cancellation, module inventory, usage feedback, and top-level error translation apply to
the whole executable. The host can own their wiring and policy adapters.

Impact categories, graph edge rules, task lifecycle, or exception authority are domain
behavior. Moving them into startup code would make focused tests difficult and create a
second hidden module.

## Providers implement seams, not policy

A CI provider knows how to list runs and retrieve bounded logs. The CI module decides
which operations are safe and how evidence is normalized. An agent adapter knows how to
invoke Codex or Claude; the Agent module owns task eligibility, permissions, isolation,
and import semantics.

The host registers both sides. It does not let the transport redefine the product rule.

## Fail composition before work begins

Duplicate commands, missing required services, incompatible provider contracts, or
invalid configuration should fail during startup or provider diagnosis. Discovering the
problem after a repository mutation begins creates unsafe partial behavior.

## Test the root as a boundary

Architecture tests protect dependency direction. Host tests enumerate required modules,
reject duplicates, verify command dispatch, and check output and exit semantics. Packaged
tool smoke tests prove that explicit assembly loading still works after build layout and
dependency resolution change.

## Recognize composition leakage

Warning signs include command handlers in `Cis.Host` that edit domain files, modules
resolving services through a global container, abstractions referencing concrete provider
packages, and target repositories influencing loaded assemblies. Each shortcut makes
behavior harder to test and the trust boundary harder to explain.

The corrective direction is simple: define the smallest public contract, move semantics
to the owning module, and leave concrete wiring in the host.

## Support alternative clients honestly

Because the host composes one domain implementation, the terminal, VS Code extension,
MCP adapter, tests, and future clients can invoke the same behavior. A client may add
presentation and local preferences; it does not need to reproduce dependency injection or
domain rules.

## Takeaway

Keep object construction and cross-cutting wiring in one host. Keep public contracts in
abstractions and domain behavior in capability modules. The boundary improves safety,
testability, and extension clarity.

## Canonical CIS sources

- [Technical intent](../specs/technical-intent-spec.md)
- [System context](../specs/system-context-spec.md)
- [Module catalogue](../specs/module-catalog-spec.md)

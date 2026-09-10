---
title: "Provider Contracts and Explicit Assembly Loading"
type: article
status: Active
series: "Building Change Impact Studio"
series_order: 8
owner: "Andrew Spiteri"
last_reviewed: "2026-09-10"
review_cadence: on provider architecture change
summary: "Replaceable transports implement stable contracts and are loaded deliberately rather than discovered in target repositories."
cis:
  stable_id: change-impact-studio:article:provider-contracts-explicit-loading
---

# Provider contracts and explicit assembly loading

Extensibility can weaken a repository tool's trust boundary if it scans target projects
for code to execute. CIS separates provider extensibility from repository discovery.

## Stable contracts define the seam

AI, agent, tracker, CI, reference, frontend-context, graph-augmentation, test/security
result, and renderer providers implement product-neutral contracts for capabilities,
configuration, requests, results, and errors. The domain module owns governance semantics;
the provider owns its technology- or transport-specific boundary.

## Assemblies are loaded explicitly

Built-in and separately packaged provider assemblies are known to the host. Target
repository binaries remain evidence, never executable plugins.

## Load order cannot decide behavior

When providers offer mutually exclusive applicable capabilities, CIS stops for explicit
selection. First registration does not become product policy.

## Credentials remain environmental

Provider configuration may be canonical, but secret values come from the environment
and never enter repository documents or result envelopes.

## Separate discovery from execution

CIS discovers source files, frameworks, packages, tests, and build artifacts in a target
repository. Those items are evidence about the governed product. Treating a discovered
assembly as an executable extension would cross from observation into code execution
without an authorization boundary.

Providers instead come from known built-ins or explicitly configured packages under host
control. Their identity and capability are visible before they receive a request.

## The domain owns semantics

A provider contract defines bounded requests, normalized results, capabilities, errors,
cancellation, and configuration. The owning module decides lifecycle and authority.

For example, a tracker adapter can create or update an issue, but the Tracker module
decides conflict behavior and prevents remote closure from approving completion. An agent
adapter can stream a provider run, but the Agent module owns task eligibility, isolation,
permissions, result validation, and import. A renderer can produce an artifact, but design
approval remains human.

## Capability negotiation is explicit

Providers differ in transports, versions, platforms, permissions, continuation, output
formats, and authentication. Diagnosis reports those facts. A caller selects an eligible
provider or receives a precise failure; load order and “first available” do not decide a
material product route.

Automatic selection, where allowed, follows canonical policy such as local-first model
routing. It never escalates content to a remote provider implicitly.

## Contain credentials and content

Canonical profiles may name providers, endpoints, capabilities, and environment-variable
keys. Secret values remain provider-native or environmental and never enter tasks,
envelopes, logs, or repository documents.

The request also carries a content boundary. Authorization to load a provider does not
authorize sending every repository source to it.

## Normalize without erasing provenance

Common result shapes let modules compare outcomes across providers. They retain provider,
version, transport, request identity, artifact digests, and limitations so normalization
does not imply identical capability. Unknown or malformed output fails validation rather
than being guessed from prose.

## Test providers as hostile boundaries

Use fakes and fixtures for timeouts, cancellation, oversized output, malformed events,
partial results, retries, authentication ambiguity, permission requests, and recovery.
Architecture and package tests verify that only intended assemblies load and target
repositories cannot inject implementations.

## Version the seam deliberately

Changing a provider request, result enum, capability, or error meaning can break separately
packaged adapters. Additive evolution needs defaults and capability negotiation; breaking
changes need a coordinated version and release path. The host should reject incompatible
packages clearly rather than failing during a live repository operation.

## Keep product fallback explicit

If a provider is unavailable, the owning module decides whether another configured route,
portable handoff, manual operation, or hard failure is valid. The provider layer must not
select a fallback that changes privacy, permissions, evidence quality, or authority.

## Takeaway

Make providers replaceable through stable contracts and deliberate host registration.
Do not execute code discovered in governed repositories or let load order resolve meaning.

## Canonical CIS sources

- [System context](../specs/system-context-spec.md)
- [Technical intent](../specs/technical-intent-spec.md)
- [External tracker synchronization](../specs/external-tracker-synchronization-spec.md)
- [Task-type extension policy](../specs/task-type-extension-policy.md)
- [Provider-neutral agent execution](../specs/features/agent-execution-coordination-feature.md)
- [CI investigation and safe operations](../specs/ci-investigation-and-operations-spec.md)
- [Reference governance and drift](../specs/reference-governance-and-drift-spec.md)

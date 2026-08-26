---
title: "Provider Contracts and Explicit Assembly Loading"
type: article
status: Draft
series: "Building Change Impact Studio"
series_order: 8
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: on provider architecture change
summary: "Replaceable transports implement stable contracts and are loaded deliberately rather than discovered in target repositories."
cis:
  stable_id: change-impact-studio:article:provider-contracts-explicit-loading
---

# Provider contracts and explicit assembly loading

Extensibility can weaken a repository tool's trust boundary if it scans target projects
for code to execute. CIS separates provider extensibility from repository discovery.

## Stable contracts define the seam

Tracker and similar providers implement product-neutral contracts for capabilities,
configuration, requests, results, and errors. The domain module owns synchronization
semantics; transports own GitHub- or Jira-specific communication.

## Assemblies are loaded explicitly

Built-in and separately packaged provider assemblies are known to the host. Target
repository binaries remain evidence, never executable plugins.

## Load order cannot decide behavior

When providers offer mutually exclusive applicable capabilities, CIS stops for explicit
selection. First registration does not become product policy.

## Credentials remain environmental

Provider configuration may be canonical, but secret values come from the environment
and never enter repository documents or result envelopes.

## Takeaway

Make providers replaceable through stable contracts and deliberate host registration.
Do not execute code discovered in governed repositories or let load order resolve meaning.

## Canonical CIS sources

- [System context](../specs/system-context-spec.md)
- [Technical intent](../specs/technical-intent-spec.md)
- [External tracker synchronization](../specs/external-tracker-synchronization-spec.md)
- [Task-type extension policy](../specs/task-type-extension-policy.md)


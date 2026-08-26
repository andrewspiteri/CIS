---
title: "Why CIS Is a Modular CLI"
type: article
status: Draft
series: "Building Change Impact Studio"
series_order: 1
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: on host architecture change
summary: "A local command-line core keeps governance scriptable, testable, editor-independent, and close to repository state."
cis:
  stable_id: change-impact-studio:article:why-cis-modular-cli
---

# Why CIS is a modular CLI

Change Impact Studio coordinates repositories, documents, Git evidence, validators,
models, agents, and trackers. A hosted web application could provide a polished entry
point, but it would move authority and repository content away from the place they live.

## Local execution matches the trust boundary

The CLI operates beside the repository. Paths, Git baselines, documentation, graph
builds, and validation remain local by default. Remote transmission requires an explicit
provider route and authorization.

## Commands are automation contracts

`cis <module> <command>` works for humans, scripts, CI, agents, and editor clients.
Stable arguments, outputs, and exit codes are easier to test than UI-driven domain logic.

## Modularity keeps capabilities coherent

Repository initialization, graph queries, impact, planning, verification, and other
domains evolve independently behind public module contracts. The host composes them
without becoming the owner of every behavior.

## Clients remain optional

The Visual Studio Code extension can present trees and previews while invoking the same
CLI behavior available to another editor or terminal user.

## Takeaway

A modular local CLI keeps CIS portable, inspectable, and repository-centered. Rich
clients can improve experience without acquiring separate domain authority.

## Canonical CIS sources

- [System context](../specs/system-context-spec.md)
- [Module catalogue](../specs/module-catalog-spec.md)
- [CLI and repository initialization](../specs/cli-and-repository-initialisation-spec.md)


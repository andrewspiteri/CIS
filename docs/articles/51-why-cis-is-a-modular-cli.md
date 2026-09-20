---
title: "Why CIS Is a Modular CLI"
type: article
status: Active
series: "Building Change Impact Studio"
series_order: 1
owner: "Andrew Spiteri"
last_reviewed: "2026-09-10"
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

The same boundary now covers product definition, solution design, references, frontend
context, testing, security, CI, direct agent execution, MCP access, and local-artifact
retention. Growth adds capability owners rather than turning the host into a monolith.

## Clients remain optional

The Visual Studio Code extension can present trees and previews while invoking the same
CLI behavior available to another editor or terminal user.

## The repository is the natural integration point

CIS needs to inspect Git state, documentation, source, test artifacts, and local tools
without uploading them to a service. A command-line process can operate under the user's
existing filesystem and credential boundary. Canonical changes remain ordinary files that
can be reviewed, branched, and merged with the product.

This does not make every operation offline. AI, tracker, CI, and reference providers may
connect to external systems when configured and authorized. The difference is that remote
access is a bounded adapter decision rather than the default architecture.

## The CLI is the product API

A stable command contract includes syntax, defaults, output schemas, standard-error
behavior, exit codes, side effects, confirmation gates, and idempotency. Human-readable
text is one client; JSON, agent output, scripts, CI, MCP, and the editor are others.

That pressure improves design. A command cannot rely on a hidden button state or mutable
browser session. It must make repository, workspace, baseline, target, and authority
explicit enough for every client to produce the same behavior.

## Modules reflect product capabilities

Each top-level command has an owner that can evolve and be tested independently. The
Graph module owns graph construction and query semantics; Planning owns task derivation
and coverage; Agent owns provider runs and result ingestion; References owns governed
source reconciliation. Public contracts let them cooperate without reaching into each
other's internal files.

A modular monolith is useful here. CIS can ship as one tool and one process while keeping
capability boundaries visible. Distributed services would add deployment and consistency
cost without improving the local repository trust model.

## Cross-cutting behavior remains consistent

The host applies common output formats, diagnostics, exit-code conventions, cancellation,
usage recording, and provider registration. Modules do not each invent their own JSON
envelope or confirmation semantics. At the same time, the host does not implement their
domain rules.

## Rich clients become projections

The VS Code extension can guide onboarding, show six activity views, stream agent runs,
and open canonical files. MCP can expose bounded operations to another client. Because
both invoke the CLI or stable contracts, they do not need parallel implementations of
impact, lifecycle, or acceptance.

## Accept the trade-offs explicitly

A CLI requires careful discoverability, structured errors, and documentation. Long-lived
interactive experiences need progress and event contracts rather than in-memory UI state.
Cross-platform paths and process behavior require strong testing. Those costs are
worthwhile because they produce a scriptable, replaceable client boundary and keep
authority close to the repository.

## Review a new capability

When adding a top-level capability, ask which domain owns its authority, which public
contracts it consumes, what canonical and derived state it creates, which remote or
filesystem boundaries it crosses, and how every client invokes it. Then verify focused
module tests, host registration, structured output, manuals, and packaged-tool behavior.
That review keeps “add one command” from becoming an invisible architectural shortcut.

## Takeaway

A modular local CLI keeps CIS portable, inspectable, and repository-centered. Rich
clients can improve experience without acquiring separate domain authority.

## Canonical CIS sources

- [System context](../specs/system-context-spec.md)
- [Module catalogue](../specs/module-catalog-spec.md)
- [CLI and repository initialization](../specs/cli-and-repository-initialisation-spec.md)

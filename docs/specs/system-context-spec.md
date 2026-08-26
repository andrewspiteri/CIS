---
title: "Change Impact Studio System Context"
type: specification
status: Draft
scope: Product
owner: "Andrew Spiteri"
last_reviewed: "2026-08-25"
review_cadence: on architecture change
cis:
  stable_id: change-impact-studio:spec:system-context
---

# Change Impact Studio system context

## Purpose

Change Impact Studio is a local engineering control layer around repository-backed
software change. It coordinates intent, context, impact review, decisions, bounded
planning, execution preparation, verification, and learning while leaving source
control, coding, issue hosting, continuous integration, and deployment in their
existing systems.

The executable boundary is the modular `cis` .NET command-line tool. The Visual Studio
Code extension is a thin client over stable CLI output and repository-owned Markdown.

## System boundary

Inside the CIS product boundary:

- the `cis <module> <command>` host and explicitly loaded modules;
- repository and workspace configuration beneath `.cis/`;
- canonical documentation structures created or governed by CIS;
- derived local indexes, graphs, caches, workflow runs, task envelopes, feedback, and
  diagnostics beneath `.cis/local/`;
- Git inspection required to establish baselines and compare actual change;
- provider-neutral contracts for models, trackers, agents, and deterministic renderers;
- the thin Visual Studio Code navigation, status, preview, and task surface; and
- validation and packaging of the CLI and editor client.

Outside the CIS product boundary:

- editing or hosting source control repositories;
- pull-request review, merge authority, CI/CD, deployment, and release orchestration;
- general issue, backlog, sprint, or portfolio management;
- autonomous stakeholder, architecture, risk, plan, design, or completion approval;
- hosted storage of repository knowledge; and
- execution of arbitrary assemblies or scripts discovered in a target repository.

## Logical components

| Component group | Responsibility | Primary implementation |
|---|---|---|
| Contracts and host | Public module contracts, command registration, dependency composition, output, exit codes, and usage capture | `src/Cis.Abstractions`, `src/Cis.Host`, `src/Cis.Modules.Host` |
| Repository governance | Repository/workspace initialization, classification, documentation, skills, standards, and diagnosis | `src/Cis.Modules.Repository`, `Docs`, `Skills`, `Standards` |
| Context | File routing, typed graph construction, queries, bounded packs, and API inventory/compatibility | `src/Cis.Modules.Index`, `Graph`, `Context`, `Api` |
| Intent and change | Business requirements, technical intent, change dossiers, impact, decisions, design, and planning | `src/Cis.Modules.Brd`, `TechnicalIntent`, `Change`, `Impact`, `Decision`, `Design`, `Plan` |
| Execution coordination | Deterministic generation, model routes, workflows, agent envelopes, and external tracker projection | `src/Cis.Modules.Generate`, `Ai`, `Workflow`, `Agent`, `Tracker`; `src/Cis.Providers.Tracker.*` |
| Assurance and learning | Planned-versus-actual verification, bounded diagnostics, feedback evidence, and reviewed learning | `src/Cis.Modules.Verify`, `Diagnostics`, `Feedback`, `Learn` |
| Editor client | Repository trees, Markdown preview, CLI status, and task visibility without product-domain ownership | `vscode-extension/` |
| Verification suites | Host, module, delivery, provider, and end-to-end behavior checks | `tests/` |

The authoritative module-by-module contract is the
[module catalogue](module-catalog-spec.md).

## Actors and external systems

| Actor or system | Relationship | Authority boundary |
|---|---|---|
| Repository maintainer | Configures CIS, documentation roots, standards, providers, and delivery policy | Owns repository policy and reviewed canonical content |
| Product or technical authority | Supplies intent, decisions, rationale, approvals, and acceptance | CIS may validate and record this authority but cannot invent it |
| Developer or coding agent | Consumes bounded work and produces a candidate implementation/result | May propose scope expansion; cannot approve or accept its own work |
| Git repository and Git CLI | Supplies repository identity, baselines, history, and actual diffs | Git evidence is observed; CIS does not commit, push, merge, or release implicitly |
| GitHub Issues or Jira Cloud | Receives a projection of governed work and returns external state | The change dossier remains canonical; conflicts require explicit reconciliation |
| Local or authorized remote model | Assists routes that benefit from interpretation or generation | Model output remains proposed; remote content transmission requires explicit permission |
| Local deterministic tools | Supply builds, tests, scanners, renderers, and workflow results | An exit code or artifact is evidence, not acceptance authority |
| Visual Studio Code | Presents repository-owned state and invokes CLI operations | The extension contains no product-domain authority |
| CI/CD and deployment systems | Validate, package, deploy, and observe accepted changes | Operate after or alongside CIS; release authority remains external and human-governed |

## Information and authority flows

```text
Canonical intent and repository evidence
        ↓
Disposable index and typed graph
        ↓
Baseline-bound impact proposals
        ↓
Human disposition and decisions
        ↓
Approved bounded plan
        ↓
Human or agent execution
        ↓
Independent Git and validation evidence
        ↓
Human acceptance
        ↓
Reviewed documentation and learning updates
```

Canonical records flow through repository-owned Markdown and structured files. Derived
state may route, cache, summarize, or project those records but may not become the only
location of durable meaning.

## Trust and data boundaries

- Target repositories are data sources. CIS never discovers and executes their
  assemblies as product modules.
- Built-in modules and separately packaged providers are loaded explicitly by the host.
- Standard output remains parseable; diagnostics use standard error; structured commands
  provide human, JSON, and agent-oriented formats.
- Repository content stays local unless an exact governed route and explicit user action
  authorize transmission to a remote provider.
- Secrets, credentials, private keys, certificates, and sources marked sensitive are not
  valid model or diagnostic inputs.
- Environment variables provide tracker and remote-provider credentials; canonical
  repository documentation does not store their values.
- External issue state, model output, task results, workflow runs, and diagnostics cannot
  establish approval, completion, risk acceptance, or release authority.

## Constraints and invariants

- The composition root is `Cis.Host`; public module contracts live in `Cis.Abstractions`.
- Each module owns one top-level `cis <module>` command and is registered explicitly.
- Canonical files are never silently overwritten, deleted, or replaced by generated state.
- Initialization and reconciliation are previewable, collision-aware, and idempotent.
- Stable identities, source digests, baselines, evidence, and human rationale survive
  re-analysis and provider projection.
- Mutually exclusive provider capabilities require an explicit selection rather than load-order resolution.
- The graph is a routing and reasoning aid, not proof that every semantic impact exists.
- Successful execution is distinct from governed verification and human acceptance.

## Related documents

- [Product intent](product-intent-spec.md)
- [Technical intent](technical-intent-spec.md)
- [Module catalogue](module-catalog-spec.md)
- [Context model and local graph](context-model-and-graph-spec.md)
- [Execution, assurance, diagnostics, and learning](execution-assurance-and-learning-spec.md)
- [Visual Studio Code client](vscode-client-spec.md)

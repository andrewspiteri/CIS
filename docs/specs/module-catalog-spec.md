---
title: "Change Impact Studio Module Catalog"
type: specification
status: Draft
version: "0.2"
scope: "Product:ChangeImpactStudio"
owner: "Andrew Spiteri"
last_reviewed: "2026-09-01"
review_cadence: "on change"
---

# Change Impact Studio Module Catalog

## 1. Purpose

This specification defines the initial CIS command modules, their responsibilities,
and the boundaries between them. It translates the capabilities proven in the PARR
toolkit into a product-oriented CLI without copying PARR's repository-specific
implementation.

Every command follows the grammar:

```text
cis <module> <command> [arguments] [options]
```

The catalog is architectural: a module owns a coherent capability, not merely a
folder or a collection of command handlers.

## 2. Design rules

- The host is only the composition root and command dispatcher.
- Each built-in module is an explicitly loaded assembly.
- A module owns one top-level command and its subcommands.
- Modules share contracts and services; they do not invoke one another's command
  implementations.
- Target repositories never provide executable assemblies for automatic discovery.
- Canonical changes remain human-reviewable and are never made silently.
- Human, JSON, and agent-oriented output represent the same result.
- The Visual Studio Code extension remains a thin client over commands and
  repository-owned records.

## 3. PARR capability mapping

| PARR capability | CIS module ownership |
|---|---|
| Repository index and repository cards | `repo`, `docs` |
| `parrdoc` documentation classification | `docs`, `graph` |
| `parrctx` context discovery | `repo`, `context` |
| `parrgen` deterministic generation and reusable visual templates | `generate`, `design` |
| `parrai` deterministic workflows | `workflow` |
| `parrai` provider routing and usage | `ai` |
| `parrdiag` runtime evidence | `diagnostics` |
| `parrdiag` learning and proposals | `learn` |
| Issue and implementation packs | `change`, `plan`, `tracker` |
| Completion and CI evidence | `test`, `security`, `verify` |

The mapping separates reusable product concepts from the original toolkit's command
layout. CIS can therefore evolve its contracts without preserving accidental PARR
coupling.

## 4. Module catalog

### 4.1 Platform and repository modules

| Module | Responsibility | Initial commands |
|---|---|---|
| `host` | Host identity, loaded modules, and installation health | `modules`, `info`, `doctor` |
| `repo` | Repository initialization, configuration, discovery, refresh, and health | `init`, `status`, `scan`, `refresh`, `doctor`, `config show` |
| `docs` | Documentation inventory, classification, proposals, application, and validation | `inventory`, `propose`, `apply`, `validate`, `catalog`, `show` |
| `standards` | Normative rule inventory, applicability routing, import, audit, compiler-graph pattern inference, structural validation, and conformance evidence | `inventory`, `applicable`, `validate`, `conformance`, `import`, `audit`, `patterns`, `infer` |
| `graph` | Construction, validation, query, tracing, and export of repository relationships | `build`, `validate`, `find`, `related`, `trace`, `export` |
| `context` | Search and bounded context packages for contracts, symbols, references, callers, and tests | `search`, `pack`, `contract`, `symbol`, `references`, `callers`, `tests-for` |
| `index` | Incremental, non-authoritative per-file routing summaries and freshness | `build`, `status`, `find` |
| `api` | API discovery, canonical contract correlation, governance validation, and compatibility | `discover`, `inventory`, `validate`, `diff` |
| `references` | Provider-based non-API reference discovery, canonical correlation, validation, and Git-baseline drift | `discover`, `inventory`, `validate`, `diff` |
| `frontend` | Provider-based web, native, and Godot screen, route, component, navigation, state, API-client discovery and graph augmentation | `discover`, `inventory`, `validate` |

### 4.2 Execution and intelligence modules

| Module | Responsibility | Initial commands |
|---|---|---|
| `generate` | Deterministic template discovery, validation, and rendering | `templates`, `describe`, `validate`, `render` |
| `workflow` | Deterministic multi-step workflow execution and run records | `list`, `describe`, `run`, `status`, `log`, `summarise` |
| `ci` | Provider-neutral remote checks, runs, jobs, bounded logs, artifacts, diagnosis, reproduction, and safe reruns | `providers`, `status`, `runs`, `jobs`, `logs`, `artifacts`, `diagnose`, `reproduce`, `rerun-failed` |
| `ai` | Provider-neutral routing, runtime model qualification, task-class approval, usage, evaluation, and cache visibility | `status`, `routes`, `providers`, `models`, `usage`, `evaluate`, `model probe`, `model benchmark`, `model approve`, `model status`, `route explain`, `eval prompt-regression`, `cache status` |
| `mcp` | Fixed-repository local stdio adapter over CIS reads and explicitly confirmed mutations | `serve` |
| `artifacts` | Local derived-state inventory, policy preview, verified reversible compaction, retrieval, conflict-aware restore, and hash-preserving cleanup records | `inventory`, `plan`, `compact`, `clean`, `archives`, `retrieve`, `restore` |
| `feedback` | Sanitized tool-usage evidence, possible token savings, aggregation, and deterministic improvement opportunities | `summary`, `usage`, `opportunities` |
| `diagnostics` | Runtime evidence sources, summaries, event streams, and analysis | `sources`, `summary`, `events`, `tail`, `analyse` |

### 4.3 Change-delivery modules

| Module | Responsibility | Initial commands |
|---|---|---|
| `change` | Change dossier identity, lifecycle, state, and closure | `create`, `list`, `show`, `status`, `close` |
| `brd` | Workspace business requirements, evidence intake, independent-review dispositions, feature-spec absorption, content currency, high-level backlog decomposition, and human authority | `discover`, `init`, `reconcile`, `status`, `validate`, `questions list`, `questions guidance`, `questions suggest`, `questions answer`, `review init`, `review status`, `review freshness`, `review decide`, `review accept-all`, `review approve`, `approve`, `backlog build`, `backlog validate`, `backlog status`, `backlog approve`, `backlog start`, `feature validate`, `feature status`, `feature approve` |
| `technical-intent` | Workspace technical direction, BRD and participant baselines, decisions, currency, and human authority | `init`, `status`, `validate`, `approve` |
| `solution-design` | Atomic overall architecture and component-sheet projection, source currency, traceability, and human authority | `init`, `status`, `validate`, `approve` |
| `ui-direction` | Workspace-level UI questionnaire, look-and-feel projection, framework and guideline provenance, lifecycle, and human authority | `questions init`, `questions status`, `questions answer`, `init`, `status`, `validate`, `approve` |
| `definition` | Resumable high-level product-definition coordination, diagrams, dictionary index, UI preview, and consolidated transactional activation | `init`, `status`, `prepare`, `answer`, `activate` |
| `impact` | Change and policy impact discovery, findings, review dispositions, and completeness | `analyse`, `findings`, `accept`, `reject`, `defer`, `completeness`, `policy analyse` |
| `decision` | Change-local decisions and promotion into durable records | `list`, `create`, `resolve`, `defer`, `promote` |
| `plan` | Bounded work, provider capability selection, task-type migration, dependencies, approval gates, and plan validation | `build`, `import-spec`, `show`, `validate`, `approve`, `status`, `capability status`, `capability select`, `task transition`, `task migrate-type` |
| `design` | Textual-wireframe review, cross-feature approved-artifact reuse, reusable shell/component renderer scaffolding, deterministic PNG generation, validation, and global human approval | `templates`, `reuse`, `scaffold`, `wireframe-validate`, `wireframe-approve`, `wireframe-reject`, `render`, `validate`, `reconcile`, `approve`, `reject`, `status` |
| `tracker` | Provider-neutral external issue projection, durable identity, three-way drift detection, and human conflict resolution | `plan`, `push`, `pull`, `status`, `resolve` |
| `agent` | Provider-neutral task envelopes, Codex/Claude execution coordination, durable run provenance, and explicit result ingestion | `providers`, `provider diagnose`, `prepare`, `run`, `author brd`, `author feature`, `review brd`, `revise brd`, `incorporate brd-questions`, `runs`, `show`, `cancel`, `recover`, `resume`, `import-result`, `status`, `evidence validate` |
| `verify` | Baselines, planned-versus-actual comparison, evidence, and acceptance | `diff`, `compare`, `validate`, `evidence`, `accept` |
| `learn` | Evidence collection, improvement proposals, review, application, and history | `collect`, `propose`, `review`, `apply`, `history` |

Direct execution is foreground, permission-bounded, and isolated by default. Provider adapters
normalize their native protocols into durable local runs; they cannot approve plans or designs,
transition tasks, accept verification, or close a change. Portable envelopes remain available
when direct execution is unavailable or deliberately not selected.

## 5. Dependency direction

```text
repo
|-- workspace --> repository authority + registry
|-- docs --> standards --> graph
|-- brd --> workspace + docs + graph
|-- technical-intent --> brd + standards + graph
|-- solution-design --> technical-intent + repository catalogue
|-- ui-direction --> solution-design + design guidelines + UI framework profiles
|-- definition --> brd + technical-intent + solution-design + ui-direction + graph
|-- context
|-- generate
|-- workflow
|-- ai
`-- diagnostics

change
|-- impact   --> context + graph + ai
|-- decision --> docs
|-- plan     --> impact + decision + context
|-- design   --> change + repository design guidelines
|-- tracker  --> plan + provider assemblies
|-- agent    --> plan + context + generate + workflow + ai
|-- verify   --> plan + workflow + graph
`-- learn    --> verify + diagnostics + workflow + ai + docs
```

These arrows describe service and contract dependencies, not command-to-command
calls. Shared data contracts belong in neutral abstractions packages when more than
one module needs them.

## 6. Delivery sequence

The initial implementation order is:

1. `host` and module contracts;
2. `repo init`, multi-repository import/list, workspace registration, and repository configuration;
3. `docs` inventory and validation;
4. `graph` and `context` foundations;
5. `workspace`, `brd`, `change`, `impact`, `decision`, and `plan`;
6. per-file `index`, provider status and generation routing in `ai`, then `generate`, `workflow`, and `agent`;
7. host-level `feedback`, then `verify`, `diagnostics`, and broader governed `learn` proposals;
8. a thin Visual Studio Code client over stable CLI contracts.

This order establishes repository ownership and deterministic evidence before adding
model-assisted behavior.

The shared graph/context data contract, provenance rules, canonical boundary, and
local storage model are defined in `context-model-and-graph-spec.md`.

## 7. First implemented product slice

`Cis.Modules.Repository` is the first module implemented after the host. Its initial
command is:

```text
cis repo init --root <repository-relative-path>
cis repo import --workspace <workspace> --source <repository>... --root <repository-relative-path>
cis workspace init --root <repository-relative-path>
cis brd discover --workspace <workspace>
```

The detailed safety and repository-layout contract is defined in
`cli-and-repository-initialisation-spec.md`.

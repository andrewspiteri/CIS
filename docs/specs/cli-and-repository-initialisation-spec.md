---
title: "Change Impact Studio — CLI and Repository Initialisation"
type: specification
status: Draft
version: "0.2"
scope: "Product:ChangeImpactStudio"
owner: "Andrew Spiteri"
last_reviewed: "2026-08-09"
review_cadence: "on change"
---

# Change Impact Studio CLI and Repository Initialisation

## 1. Purpose

This specification records the agreed direction for:

- the modular Change Impact Studio CLI;
- assembly-based module registration;
- the boundary between the CLI engine and user interfaces;
- target-repository initialisation;
- explicit ecosystem, product, owned-repository, and directional-dependency boundaries;
- the required documentation-root parameter;
- the minimum structure inside the selected root;
- canonical and derived repository state.

It refines the Change Impact Studio BRD without replacing its business requirements.

Classification, starter binding, and repeatable reconciliation are further defined in
`classification-driven-initialisation-spec.md`.

One workspace governs one product. Repositories outside that product may be imported only
as explicit producer, consumer, or bidirectional dependencies with optional component
scope. Their source informs integration context but remains outside this workspace's BRD,
implementation routing, write-capable agent work, and approval authority. Workspace schema
version 2 is required; CIS intentionally provides no implicit compatibility inference for
unqualified registries.

## 2. CLI architecture

Change Impact Studio is a CLI-first engine using:

```text
cis <module> <command> [arguments] [options]
```

Examples:

```bash
cis repo init --root docs/cis
cis repo import --workspace C:\work\commerce --source C:\work\api C:\work\web --root docs/cis --participation owned --relationship none --dry-run
cis repo list --workspace C:\work\commerce
cis workspace init --root docs --ecosystem commerce --product ordering --dry-run
cis repo doctor
cis docs inventory
cis docs validate
cis graph build
cis graph build --workspace C:\work\commerce
cis brd discover --workspace C:\work\commerce
cis brd init --workspace C:\work\commerce
cis brd reconcile --workspace C:\work\commerce
cis change create --title "Customer-owned API keys"
cis impact analyse CIS-0001
cis plan build CIS-0001
cis agent prepare CIS-0001 --work-item WI-003
cis verify diff CIS-0001
```

The CLI owns product behaviour. Visual Studio Code and other UIs are thin clients over the CLI and repository-owned records.

## 3. Module model

### 3.1 Initial modules

| Module | Responsibility |
|---|---|
| `repo` | Repository initialisation, configuration, status, and health |
| `workspace` | Documentation-authority initialization and workspace ownership |
| `docs` | Documentation inventory, proposals, application, and validation |
| `graph` | Graph construction, validation, and queries |
| `brd` | Business-requirements discovery, reconciliation, currency, validation, and human approval |
| `change` | Change dossier creation, state, and closure |
| `impact` | Impact discovery, findings, review, and approval records |
| `decision` | Change-local decisions and durable decision promotion |
| `plan` | Bounded work, dependencies, approval gates, and validation |
| `agent` | Provider-neutral task-envelope preparation and result import |
| `verify` | Baseline, diff, planned-versus-actual analysis, and completion evidence |

Responsibility must remain separated rather than accumulating in one command handler.

### 3.2 Assembly registration

Each loaded module assembly registers:

- its module name;
- required services;
- commands and subcommands;
- validation rules;
- structured result types;
- optional repository capabilities.

A conceptual contract is:

```csharp
public interface ICisModule
{
    string Name { get; }

    void RegisterServices(IServiceCollection services);

    void RegisterCommands(
        ICisCommandRegistry commands,
        IServiceProvider services);
}
```

The exact .NET and command-framework types will be selected during implementation.

### 3.3 Loading boundary

Built-in modules are loaded explicitly from the CIS distribution manifest.

CIS must not scan a target repository and execute arbitrary assemblies found there. Future third-party modules require explicit installation and configuration.

### 3.4 Common command behaviour

Where applicable, commands use consistent options:

```text
--repo <path>
--format human|json|agent
--output <path>
--dry-run
--strict
--verbose
```

Commands must:

- use stable exit codes;
- separate structured output from diagnostics;
- support machine-readable results for agents and UIs;
- report files created or changed;
- avoid silent canonical mutation;
- provide dry-run output before consequential writes where practical.

## 4. User-interface boundary

Visual Studio Code may:

- invoke CIS commands;
- display progress and results;
- provide repository and change Tree Views;
- open generated Markdown and native Git diffs;
- expose review actions;
- refresh when records change;
- optionally visualize graph data.

It must not independently implement repository classification, graph semantics, impact analysis, decision state, planning, AI routing, task semantics, or verification rules.

## 5. Repository initialisation

### 5.1 Command contract

```bash
cis repo init --root <documentation-root>
```

The repository defaults to the current working directory. An explicit repository may be supplied:

```bash
cis repo init --repo C:\projects\example --root docs/change-impact
```

### 5.2 Required root parameter

`--root` is required and defines the single documentation root managed by CIS.

Examples:

```bash
cis repo init --root docs
cis repo init --root docs/cis
cis repo init --root engineering
```

This resolves conflicts with existing `docs/` folders by making the location an explicit maintainer decision. For established repositories, the recommended default is:

```bash
cis repo init --root docs/cis
```

### 5.3 Root validation

The command must:

1. resolve the repository to an absolute path;
2. resolve `--root` relative to the repository;
3. reject an absolute `--root`;
4. reject a root that escapes the repository;
5. reject a root that resolves to the repository itself;
6. inspect an existing root before writing;
7. report collisions;
8. never overwrite files silently.

### 5.4 Existing roots

When the root exists, CIS retains its content, proposes only missing standard files or folders, shows intended changes, requires confirmation before modifying a non-empty root, and stops on conflicting required files.

CIS does not move or rename existing documentation during initialisation.

### 5.5 Dry run

```bash
cis repo init --root docs/cis --dry-run
```

Dry-run output identifies the resolved paths, proposed files and directories, retained files, collisions, blockers, and configuration to be written.

### 5.6 Non-interactive confirmation and exit codes

When an existing documentation root is non-empty, CIS reports the complete plan and
returns without writing. The maintainer confirms that plan by rerunning with `--yes`:

```bash
cis repo init --root docs/cis --yes
```

The initial command uses these stable result codes:

| Exit code | Meaning |
|---|---|
| `0` | Successful initialization, unchanged state, or successful dry run |
| `2` | Invalid input or unsupported output format |
| `3` | Confirmation is required before adding files to a non-empty root |
| `4` | A required path or repository configuration collides with existing content |

### 5.7 Repository doctor

```bash
cis repo doctor
```

Doctor is a read-only readiness inspection. It validates repository configuration,
previews initialization reconciliation, reports managed-content collisions and
classification warnings, and runs checks contributed by loaded modules. Each finding
has a stable code, severity, category, evidence, suggested fix, optional command, and
fixability classification.

Doctor automatically probes the local Ollama `/api/tags` endpoint. `OLLAMA_HOST`
overrides the default `http://127.0.0.1:11434` endpoint. Unavailability or the absence
of a local model is a warning and does not make an otherwise valid repository fail.
The probe lists models only; it does not submit repository content or invoke inference.
When the index module is loaded, Doctor also reports missing or stale derived per-file
routing cards and suggests a bounded index build. Repository initialization itself
remains deterministic and never invokes a model.

Doctor uses exit code `2` for invalid repository context and `5` for error-level
findings. Warnings and informational findings return `0`. Suggested fixes are not
applied automatically.

When init fails before repository configuration exists, doctor accepts the attempted
documentation root as a diagnostic hint:

```bash
cis repo doctor --repo . --root docs/cis --format agent
```

Always-seeded agent guidance records this recovery sequence. Confirmation-required
status remains a review gate and does not trigger failure recovery.

## 6. Minimum documentation structure

```text
<root>/
├── README.md
├── catalog.yml
├── architecture/
│   └── decisions/
├── specs/
├── references/
└── changes/
```

The structure is intentionally small. Additional folders are introduced only when a repository needs them.

| Path | Responsibility |
|---|---|
| `README.md` | Root purpose and navigation |
| `catalog.yml` | Canonical document identities, types, and relationships |
| `architecture/` | System structure, boundaries, and technical direction |
| `architecture/decisions/` | Durable architecture decision records |
| `specs/` | Product, feature, technical, and contract behaviour |
| `references/` | Current inventories, dictionaries, catalogues, and lookup material |
| `changes/` | Proposals, impacts, decisions, plans, tasks, and verification |

## 7. Repository configuration

Successful initialisation writes:

```yaml
# .cis/repository.yml
schema_version: 1

repository:
  id: example

documentation_root: docs/cis
```

All subsequent commands read this value; the user does not repeat `--root`.

## 8. Canonical and derived state

Tracked canonical state:

```text
.cis/repository.yml
<documentation-root>/**
```

Disposable local state:

```text
.cis/local/
.cis/cache/
.cis/runs/
```

Disposable state is reproducible and excluded from Git. The documentation root remains the human-reviewable source of truth.

Repository Doctor caches only deterministic initialization reconciliation beneath
`.cis/local/status/`. The cache key binds relevant repository file metadata, the selected
documentation root, and the executing repository module. It is invalidated by repository
or tool changes and may be bypassed with `cis repo doctor --refresh`. Environment probes
and contributed health checks are not replaced by this initialization cache.

## 9. Change dossiers

```text
<root>/changes/CIS-0001/
├── proposal.md
├── impact.md
├── decisions.md
├── plan.md
├── agent-tasks/
├── test-cases.md
├── test-cases.csv
├── verification.md
└── events.jsonl
```

Frontend-capable dossiers also include `design.md` as the durable artifact and human
screen-approval record. Feature planning fills `test-cases.md` and its synchronized CSV
test-management projection. `verification.md` aggregates exact execution evidence
across the task pack.

Markdown carries reviewable meaning. Structured front matter or sidecar data carries stable identity, workflow state, provenance, baselines, and relationships.

## 10. MVP constraints

The first version supports one repository, one required documentation root, built-in explicitly loaded modules, local Git, repository-owned Markdown, and disposable indexes.

It does not support multiple documentation roots, arbitrary folder mappings, automatic documentation migration, repository-provided assemblies, cross-repository coordination, or VS Code-specific business logic.

## 11. Acceptance criteria

The design is implemented when:

- `cis repo init` refuses to run without `--root`;
- the root resolves safely inside the repository;
- dry-run reports the complete intended change;
- existing files are never overwritten silently;
- successful initialisation writes `.cis/repository.yml`;
- the minimum structure exists;
- later modules read the root from configuration;
- built-in assemblies register through a module contract;
- the CLI returns human, JSON, and agent-oriented results;
- a UI uses the CLI without duplicating domain behaviour.

## 12. Recorded decisions

| Decision | Outcome |
|---|---|
| Product engine | Modular CLI |
| Command grammar | `cis <module> <command> [arguments] [options]` |
| Module discovery | Explicitly loaded assemblies register commands |
| VS Code responsibility | Thin UI over CLI and repository records |
| Initialisation location | Required repository-relative `--root` |
| Documentation roots in MVP | Exactly one |
| Existing `docs/` handling | Maintainer selects `docs`, a namespaced child, or another root |
| Standard structure | README, catalogue, architecture decisions, specs, references, changes |
| Canonical state | Repository configuration and documentation-root files |
| Derived state | Reproducible local caches and run artifacts |

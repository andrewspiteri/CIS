# Change Impact Studio

Change Impact Studio is a local-first, repository-backed engineering workspace. Its engine is a modular C# command-line application:

```text
cis <module> <command> [arguments] [options]
```

The Visual Studio Code extension is a thin client over the CLI and repository-owned Markdown.

## Current slice

The repository currently implements:

- the `ICisModule` assembly contract;
- explicit module registration;
- dependency-injection composition;
- duplicate-module protection;
- a command registry;
- a built-in `host modules` diagnostic command;
- the documented initial module catalog and dependency boundaries;
- a built-in `repo init` module with safe repository-relative path validation;
- idempotent multi-repository `repo import` and validated `.cis/workspace.yml` registry;
- dry-run planning, collision detection, and confirmation for non-empty roots;
- creation of the minimum documentation structure and `.cis/repository.yml`;
- deterministic C#/.NET, Angular, Next.js/React/Vue, Swift, Kotlin/Android,
  messaging, persistence, and Terraform classification;
- always-seeded product-intent, technical-intent, system-context, delivery-and-assurance,
  API design/governance, and public-endpoint caching specifications;
- reusable feature-specification, ADR, design-guideline, and standards templates;
- thirty curated PARR-informed, product-neutral workflow skills plus classification-selected implementation skill packs, including repository
  bootstrap and init-failure doctor routing, plus repository-wide guidance;
- classification-bound instructions and specification/reference families;
- deterministic best-effort seeding for API, command, event, workflow-state, projection,
  permission, configuration, package, route, problem, module, data, and traceability references;
- modular `api discover`, `api inventory`, `api validate`, and `api diff` commands with
  normalized `.cis/local/api/` state, PARR-derived rule enforcement, and OpenAPI compatibility findings;
- provider-neutral `tracker plan`, `push`, `pull`, `status`, and `resolve` commands with
  durable task links, three-way drift detection, conflict persistence, and human authority boundaries;
- separately loaded GitHub Issues and Jira Cloud tracker transports with environment-only credentials;
- governed AI routes, sanitized usage, disposable caching, and explicit remote authorization;
- deterministic repository-template discovery, validation, rendering, and content-hash evidence;
- shell-free checkpointed workflows with dependency validation, resumable runs, logs, and summaries;
- digest-bound portable agent task envelopes and stale-safe structured result ingestion;
- planned-versus-actual verification, exact evidence rows, gate validation, and human acceptance;
- bounded non-sensitive runtime diagnostics and human-reviewed learning proposals/history;
- a thin Visual Studio Code tree, Markdown preview, CLI status, and visible task client;
- manifest-backed incremental initialization when repository projects change;
- preservation of human-edited generated files, explicit reviewed `--accept-current`
  ownership reconciliation, and additive catalog reconciliation;
- read-only `repo doctor` readiness checks with modular diagnostics, suggested fixes,
  and automatic local Ollama detection;
- provider-neutral AI status with local-only automatic selection and explicitly authorized
  OpenAI-compatible remote routing;
- incremental per-file routing cards under `.cis/local/index-cards/`, with content-hash
  reuse, bounded model calls, sensitive-file suppression, freshness, and local search;
- consistent exclusion of dependency, build, and release-artifact directories from
  repository classification, context graphs, and routing-card discovery;
- automatic sanitized tool-usage records under `.cis/local/feedback/`, command-owned
  possible token-saving estimates, aggregate summaries, and deterministic opportunities;
- shared, validated repository-context resolution for command modules;
- deterministic `docs inventory` with catalog, front-matter, and inferred provenance;
- `docs validate` catalog integrity checks and optional strict warning enforcement;
- canonical standards with stable rule IDs, target/stack applicability routing, safe local/ZIP/GitHub
  import, local-first duplicate/conflict auditing, reversible quarantine, strict validation,
  rule-level conformance mappings, and explicit advisory-model limits;
- classification-selected, PARR-derived default standards for agent documentation, testing,
  secure delivery, APIs, persistence, eventing, frontend interaction, accessibility,
  observability, and edge headers without PARR-specific product assumptions;
- a versioned known-standard-pattern catalogue with provider and repository Markdown extensions,
  deterministic compiler-graph matching, explicit missing-capability reporting, preserved
  counterexamples, and non-canonical inferred-standard candidates;
- deterministic, versioned SQLite `graph build` with transactional content-hash updates for repository, document, component, governed reference,
  source-file, symbol, test, workflow-job, and dependency nodes with typed,
  evidence-backed relationships;
- workspace-wide graph build and validation with independent per-repository status,
  identity, diagnostics, and derived storage;
- indexed read-only `graph find` filtering and cycle-safe, state-aware `graph related`
  traversal with manifest/hash freshness reporting and no repository-wide JSON load;
- independent `graph validate` integrity, evidence, endpoint, freshness, sensitive
  property, and Git-tracking checks with optional strict warning enforcement;
- deterministic shortest-path `graph trace` with direction, relationship-state,
  confidence, evidence, depth, path-count, and proposal controls;
- safe idempotent `graph export` to Markdown, JSON, JSONL, and Graphviz DOT;
- focused `context contract`, `context symbol`, and `context tests-for` commands that
  preserve graph evidence and never infer verification from naming;
- registered-ID context-pack federation across imported repositories;
- authority-aware workspace initialization and BRD discovery, reconciliation,
  validation, human approval, and participant-baseline drift detection;
- human, JSON, and agent-oriented output;
- catalogued change dossiers against exact Git or graph-build baselines;
- deterministic impact proposals with stable IDs, evidence, confidence, bounded roots,
  human accept/reject/defer disposition, and completeness reporting;
- change-local blocking and advisory decisions with recorded options, evidence,
  human resolution/deferral, plan gates, and idempotent ADR promotion;
- bounded dependency-aware planning from accepted impacts, with acceptance criteria,
  validation, open-decision gates, structural validation, and human-only approval;
- host composition and command-dispatch tests.

The ten-stage local delivery lifecycle is implemented end to end. See
`docs/specs/implementation-roadmap.md` for the authoritative stage record.

## Build and run

The repository requires the .NET SDK `10.0.400` feature band. `global.json` uses
`latestPatch`, so builds may use a later installed `10.0.4xx` patch, but do not
silently cross into another .NET 10 feature band. Preview SDKs are excluded. The
target framework remains `net10.0`, independently of the selected SDK patch.

NuGet dependency versions are pinned centrally in `Directory.Packages.props`.
The .NET tool version is currently maintained explicitly in
`src/Cis.Host/Cis.Host.csproj`; `tools/build-release.ps1` builds and packs that
declared version but does not calculate or increment it.

```bash
dotnet build ChangeImpactStudio.slnx
dotnet test ChangeImpactStudio.slnx
dotnet run --project src/Cis.Host -- host modules
dotnet run --project src/Cis.Host -- host modules --format json
dotnet run --project src/Cis.Host -- repo init --root docs/cis --dry-run
dotnet run --project src/Cis.Host -- workspace init --root docs --dry-run
dotnet run --project src/Cis.Host -- repo import --workspace C:\work\workspace --source C:\work\api C:\work\web --root docs/cis --dry-run
dotnet run --project src/Cis.Host -- repo doctor
dotnet run --project src/Cis.Host -- feedback summary --since 7d
dotnet run --project src/Cis.Host -- feedback opportunities --format agent
dotnet run --project src/Cis.Host -- docs inventory --format json
dotnet run --project src/Cis.Host -- docs validate --strict
dotnet run --project src/Cis.Host -- standards applicable --target backend --stack csharp --format agent
dotnet run --project src/Cis.Host -- standards validate --strict
dotnet run --project src/Cis.Host -- standards import --source C:\work\standards --dry-run
dotnet run --project src/Cis.Host -- standards audit --format agent
dotnet run --project src/Cis.Host -- graph build --format agent
dotnet run --project src/Cis.Host -- graph build --workspace C:\work\workspace --format agent
dotnet run --project src/Cis.Host -- graph validate --strict
dotnet run --project src/Cis.Host -- brd discover --workspace C:\work\workspace --format agent
dotnet run --project src/Cis.Host -- brd init --workspace C:\work\workspace --title "Business Requirements"
dotnet run --project src/Cis.Host -- brd reconcile --workspace C:\work\workspace
dotnet run --project src/Cis.Host -- brd validate --workspace C:\work\workspace
dotnet run --project src/Cis.Host -- graph find --kind symbol --text GraphQuery
dotnet run --project src/Cis.Host -- graph related --id <node-id> --depth 2
dotnet run --project src/Cis.Host -- graph trace --from <node-id> --to <node-id>
dotnet run --project src/Cis.Host -- graph export --type markdown
dotnet run --project src/Cis.Host -- context tests-for --id <node-id>
dotnet run --project src/Cis.Host -- change create --title "Change outcome" --outcome "Observable result" --root <node-id>#<kind>
dotnet run --project src/Cis.Host -- change rebaseline CIS-0001 --actor "reviewer" --reason "Adopt reviewed pre-impact source additions"
dotnet run --project src/Cis.Host -- impact analyse CIS-0001
dotnet run --project src/Cis.Host -- impact completeness CIS-0001
dotnet run --project src/Cis.Host -- decision create CIS-0001 --question "Compatibility strategy?" --category contract --option "preserve" --option "version" --evidence "consumer inventory"
dotnet run --project src/Cis.Host -- decision resolve CIS-0001 DEC-001 --option "version" --rationale "Preserve existing consumers"
dotnet run --project src/Cis.Host -- plan build CIS-0001
dotnet run --project src/Cis.Host -- plan validate CIS-0001
dotnet run --project src/Cis.Host -- tracker plan CIS-0001 --provider github
dotnet run --project src/Cis.Host -- workflow run standard-delivery --run-id local-check
dotnet run --project src/Cis.Host -- agent prepare CIS-0001 WORK-001
dotnet run --project src/Cis.Host -- verify validate CIS-0001
dotnet run --project src/Cis.Host -- diagnostics analyse
dotnet run --project src/Cis.Host -- learn collect
```

Create a release package with `tools/build-release.ps1`. The CLI is emitted as the
`AndrewSpiteri.ChangeImpactStudio` .NET tool package. The editor client is in
`vscode-extension/`; run `node --check vscode-extension/extension.js` for its dependency-free
syntax gate.

## Repository structure

```text
src/
|-- Cis.Abstractions/          Module and registry contracts
|-- Cis.Host/                  Executable composition root
|-- Cis.Modules.Context/       Focused bounded engineering-context queries
|-- Cis.Modules.Agent/         Portable agent task envelopes and result ingestion
|-- Cis.Modules.Api/           API discovery, governance, and compatibility
|-- Cis.Modules.Diagnostics/   Bounded runtime evidence and analysis
|-- Cis.Modules.Generate/      Deterministic repository template rendering
|-- Cis.Modules.Learn/         Reviewed improvement proposals and history
|-- Cis.Modules.Tracker/       External issue projection and conflict-safe reconciliation
|-- Cis.Modules.Verify/        Delivery comparison, evidence, gates, and acceptance
|-- Cis.Modules.Workflow/      Shell-free resumable workflow execution
|-- Cis.Providers.Tracker.GitHub/ GitHub Issues transport
|-- Cis.Providers.Tracker.Jira/   Jira Cloud transport
|-- Cis.Modules.Change/        Repository-owned change dossier lifecycle
|-- Cis.Modules.Decision/      Change-local decisions and ADR promotion
|-- Cis.Modules.Docs/          Documentation inventory and validation
|-- Cis.Modules.Feedback/      Tool usage, token estimates, and feedback opportunities
|-- Cis.Modules.Graph/         Derived context graph construction and queries
|-- Cis.Modules.Host/          Built-in host diagnostics
|-- Cis.Modules.Impact/        Deterministic findings and human disposition
|-- Cis.Modules.Plan/          Bounded work, validation, and approval gates
|-- Cis.Modules.Standards/     Standards routing, validation, and conformance
`-- Cis.Modules.Repository/    Repository initialization and configuration
vscode-extension/              Thin editor client over CLI and Markdown
tests/
|-- Cis.Host.Tests/
|-- Cis.Modules.Docs.Tests/
|-- Cis.Modules.Api.Tests/
|-- Cis.Modules.Feedback.Tests/
|-- Cis.Modules.Graph.Tests/
|-- Cis.Modules.Standards.Tests/
|-- Cis.Modules.Delivery.Tests/
`-- Cis.Modules.Repository.Tests/
```

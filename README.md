# Change Impact Studio

## 1. Introduction

Change Impact Studio (CIS) is a local-first, repository-backed workspace for governing
software change from intent through acceptance.

```text
cis <module> <command> [arguments] [options]
```

Software is becoming easier to generate than it is to understand. Important context is
often scattered across requirements, source code, contracts, tests, workflows, Git
history, tickets, and individual knowledge. A developer or coding agent can therefore
produce a plausible implementation that is incomplete, inconsistent, or aimed at the
wrong outcome.

CIS creates a controlled workflow around that problem:

```text
Establish intent
    → build repository context
    → review likely impact
    → resolve decisions
    → approve bounded work
    → prepare or run bounded human/agent execution
    → compare actual change with approved scope
    → accept completion
    → improve repository knowledge
```

Canonical meaning remains in repository-owned Markdown and structured files. Local
indexes, graphs, workflow state, caches, model output, and diagnostic analysis remain
derived and rebuildable.

> AI and coding agents may discover, propose, analyse, draft, and implement. Humans
> confirm meaning, resolve material decisions, accept risk, and accept completion.

CIS complements Git, GitHub, Jira, editors, coding agents, and CI/CD. It does not
replace them or silently perform commits, pushes, merges, releases, or approvals.

## 2. Quick start

### 2.1. Installing CIS

CIS is distributed as the `AndrewSpiteri.ChangeImpactStudio` .NET tool in each GitHub
release bundle. It currently installs from the downloaded release folder rather than
from a public NuGet feed.

Prerequisites:

- Git.
- The .NET 10 runtime or SDK.
- PowerShell for the examples below.

Download a release bundle, verify its `SHA256SUMS`, and place the extracted files in a
local directory such as `C:\tools\cis-release`. The directory should contain the
`AndrewSpiteri.ChangeImpactStudio.<version>.nupkg` file.

```powershell
Set-Location C:\tools\cis-release
Get-FileHash .\* -Algorithm SHA256

$cisVersion = "<release-version>"
dotnet tool install AndrewSpiteri.ChangeImpactStudio `
  --global `
  --version $cisVersion `
  --add-source C:\tools\cis-release

cis --help
cis host modules
```

Replace `<release-version>` with the release version, without the leading `v`. Keep the
downloaded `SHA256SUMS` beside the release files so the calculated hashes can be
compared with the published values before installation.

To install into a project-local tool directory instead of globally:

```powershell
dotnet tool install AndrewSpiteri.ChangeImpactStudio `
  --tool-path .\.tools\cis `
  --version $cisVersion `
  --add-source C:\tools\cis-release

.\.tools\cis\cis --help
```

To build and install the package from source, follow [section 4](#4-cloning-and-building).

### 2.2. Starting a new repository

Use this path when creating a new Git repository whose product knowledge will be
governed by CIS from the beginning.

```powershell
New-Item -ItemType Directory C:\work\new-product
Set-Location C:\work\new-product
git init

cis workspace init `
  --root docs\cis `
  --ecosystem new-product-ecosystem `
  --product new-product `
  --dry-run --format agent

cis workspace init `
  --root docs\cis `
  --ecosystem new-product-ecosystem `
  --product new-product `
  --yes

cis docs validate --strict
cis skills validate --strict
cis standards validate --strict
cis graph build --format agent
cis graph validate --strict
cis definition init
cis definition status
```

The dry run shows every planned directory, file, standard, skill, instruction, warning,
collision, and authority record without changing the repository. Review it before passing
`--yes`. Replace the sample ecosystem and product IDs with stable identifiers for the
product being created. One workspace governs one product.

As source projects are added, rerun initialization. CIS will reconcile newly detected
languages, frameworks, roles, and capabilities without silently overwriting reviewed
content.

### 2.3. Onboarding a single existing repository

Use this path for an existing application or library that will own its product and
engineering documentation locally.

```powershell
Set-Location C:\work\orders

cis repo import `
  --workspace C:\work\orders `
  --source C:\work\orders `
  --root docs\cis `
  --participation owned --relationship none `
  --ecosystem commerce --product ordering `
  --dry-run --format agent

cis repo import `
  --workspace C:\work\orders `
  --source C:\work\orders `
  --root docs\cis `
  --participation owned --relationship none `
  --ecosystem commerce --product ordering `
  --yes

cis docs inventory
cis docs validate --strict
cis skills validate --strict
cis standards validate --strict
cis graph build --format agent
cis graph validate --strict
```

Self-import initializes the repository in place and registers that same repository as
the product authority in one reviewed transaction. It classifies repository evidence and
proposes the applicable documentation, standards, references, skills, and instructions.
Deterministically discovered material starts with a truthful review state; discovery is
evidence, not proof that the generated meaning is complete or approved.

If initialization reports an error or collision, do not repeatedly force it. Inspect
the repository with the same documentation root:

```powershell
cis repo doctor --root docs\cis --format agent
```

See the [`cis repo import` manual](docs/manual/cis_repo_import.md) for the self-import
contract and the [`cis repo init` manual](docs/manual/cis_repo_init.md) for underlying
confirmation, ownership, collision, and recoverable-quarantine behavior.

### 2.4. Initializing a multi-repository workspace

Use one documentation repository as the workspace authority when one product spans
several repositories. The workspace records a stable product identity inside its wider
software ecosystem. Product-owned repositories can receive implementation work;
external producer or consumer repositories are bounded read context only.

Initialize the authority repository first:

```powershell
Set-Location C:\work\commerce-docs

cis workspace init --root docs --ecosystem commerce --product ordering --dry-run --format agent
cis workspace init --root docs --ecosystem commerce --product ordering --yes
```

Then import the participant repositories. Import records their locations and initializes
their selected documentation roots; it does not copy, clone, move, or execute their source.

```powershell
cis repo import `
  --workspace C:\work\commerce-docs `
  --source C:\work\orders-api C:\work\orders-web C:\work\orders-infra `
  --root docs\cis `
  --participation owned --relationship none `
  --dry-run --format agent

cis repo import `
  --workspace C:\work\commerce-docs `
  --source C:\work\orders-api C:\work\orders-web C:\work\orders-infra `
  --root docs\cis `
  --participation owned --relationship none `
  --yes

cis repo list --workspace C:\work\commerce-docs
cis graph build --workspace C:\work\commerce-docs --format agent
cis graph validate --workspace C:\work\commerce-docs --strict
```

Import a repository owned by another product with `--participation dependency` and an
explicit `producer`, `consumer`, or `bidirectional` relationship. Its contracts and
components can inform technical intent, but its implementation is governed by its own
product workspace. The authority repository owns this product's business requirements
and technical intent. Those documents must be current and approved before governed
product changes can be planned.

Continue with the [workspace initialization](docs/manual/cis_workspace_init.md),
[repository import](docs/manual/cis_repo_import.md), and
[business requirements governance](docs/specs/business-requirements-governance-spec.md)
documentation.

## 3. What CIS generates inside a repository

The exact files vary with repository classification. A typical initialized repository
contains:

```text
<repository>/
├── .cis/
│   ├── repository.yml
│   ├── workspace.yml                 # workspace authority or participant registry, when used
│   ├── starter-manifest.yml
│   ├── .gitignore
│   └── local/                        # derived, disposable state
│       ├── api/
│       ├── feedback/
│       ├── graph/
│       ├── index-cards/
│       └── runs/
├── .github/
│   ├── instructions/                # repository and classification-specific guidance
│   └── skills/                      # CIS workflow and implementation skills
└── <documentation-root>/            # for example docs/cis
    ├── README.md
    ├── catalog.yml
    ├── architecture/
    │   └── decisions/
    ├── changes/                     # baseline-bound change dossiers
    ├── references/                  # inventories, profiles, dictionaries, and matrices
    ├── specs/                       # product and technical contracts
    ├── standards/                   # normative rules with stable rule IDs
    ├── templates/                   # feature, ADR, standard, and design templates
    └── workflows/                   # shell-free workflow definitions
```

The important boundary is:

- Canonical documentation, configuration, decisions, plans, and acceptance evidence are
  reviewable repository files.
- `.cis/local/` contains rebuildable indexes, graphs, caches, runs, feedback, envelopes,
  and analysis. Deleting it must not remove durable product meaning.
- Generated or discovered documents remain in their declared lifecycle state until a
  human reviews them.
- Initialization is idempotent and collision-aware. It preserves divergent human edits
  rather than silently replacing them.

`catalog.yml` gives every governed document a stable identity, type, lifecycle, path,
and authority. Use `cis docs inventory` to inspect the result and
`cis docs validate --strict` to detect missing, malformed, duplicate, or uncatalogued
documentation.

## 4. Cloning and building

### 4.1. Building from source

The CIS source repository requires the .NET SDK `10.0.400` feature band. `global.json`
accepts later `10.0.4xx` patches but does not cross into another feature band or use
preview SDKs. Node.js 22 is required for the Visual Studio Code extension and complete
release build.

```powershell
git clone https://github.com/AndrewSpiteri/change-impact-studio.git
Set-Location change-impact-studio

dotnet --version
dotnet restore ChangeImpactStudio.slnx
dotnet build ChangeImpactStudio.slnx --no-restore
dotnet test ChangeImpactStudio.slnx --no-build --no-restore

dotnet run --project src\Cis.Host -- --help
dotnet run --project src\Cis.Host -- host modules
```

While developing from source, replace `cis` in an example with:

```text
dotnet run --project <cis-repository>/src/Cis.Host --
```

For example:

```powershell
dotnet run --project C:\work\change-impact-studio\src\Cis.Host -- `
  repo doctor --repo C:\work\orders --root docs\cis
```

Build the complete release bundle and install its local tool package:

```powershell
.\tools\build-release.ps1

$cisVersion = ([xml](Get-Content Version.props -Raw)).Project.PropertyGroup.VersionPrefix
dotnet tool install AndrewSpiteri.ChangeImpactStudio `
  --tool-path .\.artifacts\cis `
  --version $cisVersion `
  --add-source .\artifacts\release

.\.artifacts\cis\cis host modules
```

The release script builds and tests the solution, validates the editor client, creates
the .NET tool and VS Code packages, smoke-tests the packaged CLI, archives tracked
source, and writes SHA-256 checksums beneath `artifacts/release`.

### 4.2. Source repository structure

```text
src/
├── Cis.Abstractions/                 public module contracts
├── Cis.Host/                         executable composition root
├── Cis.Modules.*/                    product capability modules
└── Cis.Providers.Tracker.*/          separately loaded tracker transports
tests/                                focused module and cross-module verification
docs/specs/                           canonical product and technical contracts
docs/standards/                       normative engineering expectations
docs/references/                      inventories, profiles, and conformance evidence
docs/manual/                          command reference
docs/articles/                        explanatory articles and editorial navigation
docs/changes/                         governed change dossiers
docs/architecture/                    durable architecture decisions
vscode-extension/                     thin client over the CLI and Markdown
tools/                                build, validation, packaging, and release scripts
.cis/                                 CIS repository configuration and local-state routing
```

`Cis.Host` is the only composition root. Public contracts live in `Cis.Abstractions`,
and each module owns one top-level `cis <module>` command. The Visual Studio Code
extension remains a thin client and contains no product-domain authority.

The current component responsibilities are documented in the
[module catalogue](docs/specs/module-catalog-spec.md).

## 5. Additional notes

### Documentation and articles

Use these entry points rather than browsing every Markdown file:

| Need | Start here |
|---|---|
| Product purpose and boundaries | [Product intent](docs/specs/product-intent-spec.md) and [system context](docs/specs/system-context-spec.md) |
| Architecture and implementation principles | [Technical intent](docs/specs/technical-intent-spec.md) and [overall solution-design governance](docs/specs/overall-solution-design-governance-spec.md) |
| Product-wide UI look, feel, shell, reuse, responsiveness, and accessibility | [High-level UI direction governance](docs/specs/high-level-ui-direction-governance-spec.md) |
| Eight-page product-definition journey and consolidated activation | [High-level product-definition wizard](docs/specs/high-level-product-definition-wizard-spec.md) |
| Feature intake, definition, design, delivery review, and implementation reconciliation | [`cis brd feature intake`](docs/manual/cis_brd_feature_intake.md) and [feature-wizard status](docs/manual/cis_brd_feature_wizard_status.md) |
| Command syntax and behavior | [Command manual](docs/manual/README.md) |
| Normative engineering rules | [`docs/standards/`](docs/standards/) and the [conformance matrix](docs/references/standards-conformance-matrix.md) |
| Current inventories and profiles | [References index](docs/references/README.md) |
| Product completion state | [Implementation roadmap](docs/specs/implementation-roadmap.md) |
| Governance and CIS articles | [Articles index](docs/articles/README.md) |

The articles index lists all 78 publication-ready articles by topic and reading order.
Find a specific article from the repository with:

```powershell
git ls-files "docs/articles/*.md"
rg -n -i "governance|impact|verification" docs\articles
cis docs inventory
cis graph find --kind document --text "governance"
```

Articles explain CIS but do not override canonical specifications, standards,
decisions, references, manuals, or implemented behavior.

### Starting a governed change

Once repository context and required intent are current, choose the entry point that
matches the work.

For a product feature described by a prepared BRD, begin with
[`cis brd feature intake`](docs/manual/cis_brd_feature_intake.md). Review the eight feature
pages, reconcile proposed delivery stories with existing owned implementation, and record
the final feature-definition review. This records review of the proposed definition; it
does not create or approve the feature specification, approve the product backlog,
authorize implementation, or approve a release. Complete those gates before deriving
bounded work.

For a bounded engineering change that does not start from a feature BRD, create a dossier
directly:

```powershell
cis graph find --text "concept or component"
cis graph related --id <node-id> --depth 2

cis change create `
  --title "Observable change outcome" `
  --outcome "Result a reviewer can verify" `
  --root <node-id>#<kind>

cis impact analyse CIS-0001
cis impact findings CIS-0001
cis impact completeness CIS-0001
cis plan build CIS-0001
cis plan validate CIS-0001
```

Impact findings begin as proposals. Human reviewers disposition impact and resolve
blocking decisions before plan approval. Executors produce candidate changes; CIS then
compares independently observed Git and validation evidence with the approved scope.

See [Change impact and bounded planning](docs/specs/change-impact-and-planning-spec.md)
for the complete lifecycle.

### Useful diagnostics

```powershell
cis host modules --format agent
cis repo doctor --format agent
cis docs validate --strict
cis skills audit --format agent
cis standards conformance --gaps-only
cis graph validate --strict
cis ai status
cis feedback summary --since 7d
cis feedback opportunities --format agent
```

Structured commands support human, JSON, and agent-oriented output where applicable.
Standard output remains parseable and diagnostics are written to standard error.

Provider-neutral direct execution is explicit and foreground-only:

```powershell
cis agent providers --format agent
cis agent provider diagnose codex --format agent
cis agent run CIS-0002 WORK-090 --provider codex --transport app-server `
  --mode implement --permission workspace-write --actor "Andrew Spiteri"
```

Workspace-write runs use isolated Git worktrees by default. Provider credentials remain
provider-native, network escalation is denied, run events are streamed and retained under
`.cis/local/agents/runs/`, and results require explicit import before becoming canonical evidence.

### Versioning, releases, and validation

The authoritative product version is declared in [`Version.props`](Version.props) and
must match [`vscode-extension/package.json`](vscode-extension/package.json). NuGet
dependency versions are pinned in
[`Directory.Packages.props`](Directory.Packages.props). The release policy is defined
in [Versioning and release](docs/standards/versioning-and-release.md).

The [delivery and assurance specification](docs/specs/delivery-and-assurance-spec.md)
defines proportionate focused and wider validation. Repository-specific agent guidance
is in [`AGENTS.md`](AGENTS.md).

Change Impact Studio is licensed under the [MIT License](LICENSE.md).

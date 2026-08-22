---
title: "Change Impact Studio Classification-Driven Initialisation"
type: specification
status: Draft
version: "0.3"
scope: "Product:ChangeImpactStudio"
owner: "Andrew Spiteri"
last_reviewed: "2026-08-08"
review_cadence: "on change"
---

# Classification-Driven Initialisation

## 1. Purpose

`cis repo init` is an incremental repository reconciler. It deterministically
classifies an imported repository, binds a curated set of documentation and agent
assets to that classification, and proposes only the delta required for the current
repository state.

The user confirms or corrects classifications. The user does not select every
individual specification, reference, skill, or instruction.

## 2. Pipeline

```text
scan
-> classify components
-> bind starter definitions
-> seed deterministic reference facts
-> compare previous manifest
-> plan creates and safe updates
-> review and confirm
-> apply
-> validate
```

`--dry-run` executes the pipeline through planning without changing files. `--yes`
confirms the displayed classification and artifact delta for non-interactive use.

## 3. Classification model

Classification is component-based so mixed repositories and monorepos are not forced
into one label.

```yaml
components:
  - id: example-api
    root: src/Example.Api
    languages: [csharp]
    frameworks: [aspnet-core]
    roles: [backend-api-producer]
    capabilities: [openapi, authorization]
    confidence: high
    evidence:
      - src/Example.Api/Example.Api.csproj
      - src/Example.Api/Program.cs: MapControllers
```

Classification facets are:

- repository shape: application, library, monorepo, tooling, or infrastructure;
- languages: C#, TypeScript, JavaScript, Swift, Kotlin, Python, SQL, HCL, and later registered languages;
- frameworks: ASP.NET Core, Angular, Next.js, React, SwiftUI, UIKit, Android,
  Jetpack Compose, Godot, .NET Worker, EF Core, and later registered frameworks;
- delivery surfaces: web, iOS, Android, macOS, desktop, service, worker, and infrastructure;
- roles: backend API producer or consumer, frontend consumer, worker, event producer
  or consumer, package producer or consumer, database, infrastructure, deployment,
  test automation, and tooling;
- capabilities: OpenAPI, authorization, configuration, events, persistence,
  migrations, deployment, and other evidence-backed capabilities.

Every non-trivial classification records confidence, evidence, and deterministic or
model-assisted provenance. Initialisation uses deterministic evidence only in the
first implementation.

## 4. Initial deterministic classifiers

The first scanner supports:

- C# projects from `*.csproj`;
- ASP.NET Core from Web SDK, framework references, and API source markers;
- .NET Worker and test projects from SDK/package/source evidence;
- EF Core and persistence from package and migrations evidence;
- event production or consumption from explicit messaging packages and source markers;
- Angular projects from `angular.json`, `package.json`, and project roots;
- Next.js and React applications from `package.json`, TypeScript configuration, and source markers;
- Swift packages and Xcode projects, including SwiftUI, UIKit, iOS, and macOS evidence;
- Kotlin/Gradle projects, including Android, Jetpack Compose, and Kotlin Multiplatform evidence;
- Godot projects from `project.godot`, including C#, GDScript, main-scene, autoload,
  editor-plugin, mobile-renderer, and input evidence;
- loose executable source areas without a project manifest, classified as tooling,
  test automation, or a shared library from their repository location;
- web and native frontend/API-consumer roles independent of a particular framework;
- Terraform presence as infrastructure evidence.

Dependency, legacy, and generated-output roots such as `node_modules`, `_old`,
`build_out`, Gradle `build`, `.godot`, `bin`, and `obj` are excluded before component
classification where applicable. Canonical build tooling in a repository-owned `build`
project remains classifiable; nested Gradle output does not become a component.

Ambiguous evidence produces warnings. It must not create a confident fact silently.

## 5. Starter definitions and bindings

A starter definition describes one generated asset or governed asset family. Loaded
CIS modules may register definitions, but target repositories may not provide
assemblies for automatic execution.

Reference families normally generate a pair:

```text
<root>/specs/<family>-spec.md
<root>/references/<family>.md
```

The specification governs maintenance and required fields. The reference contains
the repository's current inventory. Their catalog entries use stable IDs and
`paired_reference` or `governed_by` relationships when graph metadata is introduced.

The curated registry is product-neutral but reconciled with the reusable governance
patterns proven in PARR. It includes:

- product-intent, technical-intent, system-context, delivery-and-assurance, and
  public-endpoint-caching-policy seed
  specifications for every repository;
- reusable feature-specification and architecture-decision templates;
- API, event, permissions, configuration, package, command, and workflow-state dictionaries;
- business-invariant, projection, problem-details, and module-ownership catalogues;
- data dictionary, ERD, and traceability references;
- repository-wide, change-delivery, and business-requirements CIS instructions plus path-specific
  language/framework instructions;
- repository-bootstrap, multi-repository import, business-requirements governance, change-impact, documentation-maintenance, contract-maintenance,
  domain-behaviour-maintenance, completion-validation, ADR, verification, graph/context,
  file-index, change-dossier, impact-review, decision-review, and bounded-planning skills.

Binding rules select relevant definitions. Examples:

| Classification | Bound definitions |
|---|---|
| ASP.NET Core API | API dictionary, problem-details catalogue, configuration dictionary |
| Authorization evidence | Permissions dictionary |
| Event producer or consumer | Event dictionary |
| EF Core or migrations | Data dictionary and ERD |
| Angular | API-consumption, configuration, package, screen/route guidance |
| Next.js or React | API-consumption, configuration, package, screen/route guidance |
| Swift iOS/macOS client | API-consumption, configuration, package, screen/navigation guidance |
| Kotlin Android client | API-consumption, configuration, package, screen/navigation guidance |
| Multiple components | Module-ownership map |
| C# component | Path-specific C# instruction |
| Angular component | Path-specific Angular instruction |
| Next.js component | Path-specific Next.js instruction |
| Swift component | Path-specific Swift/Apple-platform instruction |
| Kotlin component | Path-specific Kotlin/Android instruction |

The plan explains the classification and evidence that selected each definition.

### 5.1 Always-seeded assets

Initialization creates a small baseline even when no supported language or framework
is detected:

```text
.github/instructions/cis-repository.instructions.md
.github/instructions/cis-change-delivery.instructions.md
.github/instructions/cis-business-requirements.instructions.md
.github/skills/cis-change-impact/SKILL.md
.github/skills/cis-repository-bootstrap/SKILL.md
.github/skills/cis-import-repositories/SKILL.md
.github/skills/cis-govern-business-requirements/SKILL.md
.github/skills/cis-documentation/SKILL.md
.github/skills/cis-verification/SKILL.md
.github/skills/cis-maintain-contracts/SKILL.md
.github/skills/cis-maintain-domain-behaviour/SKILL.md
.github/skills/cis-add-documentation/SKILL.md
.github/skills/cis-validate-completion/SKILL.md
.github/skills/cis-write-adr/SKILL.md
.github/skills/cis-graph-context/SKILL.md
.github/skills/cis-file-index/SKILL.md
.github/skills/cis-change-dossier/SKILL.md
.github/skills/cis-impact-review/SKILL.md
.github/skills/cis-decision-review/SKILL.md
.github/skills/cis-bounded-planning/SKILL.md
<root>/specs/product-intent-spec.md
<root>/specs/technical-intent-spec.md
<root>/specs/system-context-spec.md
<root>/specs/delivery-and-assurance-spec.md
<root>/specs/repository-delivery-policy-spec.md
<root>/specs/public-endpoint-caching-policy-spec.md
<root>/templates/feature-spec-template.md
<root>/templates/adr-template.md
<root>/references/repository-profile.md
<root>/references/ui-framework-profile.md (UI-bearing classifications only)
<root>/references/task-type-capability-selections.md
```

The specifications contain reviewable starting sections and classification-derived
component tables. They remain Draft until a maintainer verifies and completes them.

For UI-bearing components, initialization performs evidence-first UI framework
resolution. A detected framework is recorded as `Existing` and takes precedence. If
no component framework is found, a platform default is recorded as `Default`; Tailwind
CSS alone is styling evidence and does not satisfy component-framework detection. The
profile and scoped agent instruction are generated without installing dependencies.

The repository-bootstrap skill requires an explicit maintainer-selected root. It
treats confirmation-required status as a review gate and routes init errors or
collisions to `cis repo doctor --repo <repository> --root <documentation-root>` before
another initialization attempt.

The import-repositories skill plans a complete batch before mutation, requires one
reviewed confirmation, routes any failed source through repository doctor, validates
the resulting workspace registry, and builds and validates every registered graph.

The govern-business-requirements skill treats discovery as evidence rather than
currency, maintains the authority repository's canonical BRD, exposes participant
baseline drift, reconciles development feature specifications into explicit source
assessment and traceability, and reserves approval for an explicitly authorized human action.

### 5.2 Deterministic reference seeding

When evidence can be extracted without executing repository code, initialization
pre-populates Draft references. The current extractors cover:

| Reference | Deterministic sources |
|---|---|
| API dictionary | ASP.NET Core minimal-route mappings and Next.js route handlers |
| Command dictionary | C# command type declarations |
| Event dictionary | C# event type declarations |
| Workflow-state dictionary | C# state/status enumerations and their members |
| Projection dictionary | C# projection, projector, and read-model type declarations |
| Permissions dictionary | C# authorization policies and permission constants |
| Configuration dictionary | `appsettings*.json` and environment example files; likely secrets are redacted |
| Package catalogue | MSBuild package references, `package.json`, Gradle coordinates, and Swift package declarations |
| Screen and route map | Next.js file routes and Angular route declarations |
| Problem-details catalogue | C# exception type declarations |
| Module ownership map | Classified components, roots, roles, and capabilities |
| Data dictionary | Entity Framework `DbSet<T>` declarations |
| Traceability matrix | Classified components as reviewable traceability anchors |

An extractor emits only facts supported by recognized syntax. Unsupported or
ambiguous sources leave a `TODO` row rather than inventing content. Seeded rows use
the `Draft` lifecycle state and require maintainer review. Initialization never executes
target-repository assemblies or application code to obtain reference data.

## 6. Canonical and derived state

Detailed scan evidence is disposable:

```text
.cis/local/init/scan.json
```

Confirmed state is tracked:

```text
.cis/repository.yml
.cis/starter-manifest.yml
<root>/references/repository-profile.md
<root>/references/ui-framework-profile.md (UI-bearing classifications only)
<root>/catalog.yml
```

The starter manifest records stable components and managed artifacts:

```yaml
schema_version: 1
components: []
managed_artifacts:
  - id: reference.api-dictionary.spec
    path: docs/cis/specs/api-dictionary-spec.md
    definition: reference.api-dictionary
    template_version: 1
    applied_hash: sha256:...
    ownership: managed
```

The applied hash distinguishes an unchanged generated file from a user-edited file.
The manifest itself is CIS-owned structured state; generated Markdown remains
human-reviewable.

## 7. Repeatability and reconciliation

Running `repo init` repeatedly is expected.

For an unchanged repository and unchanged templates, the result is `unchanged` and
no files are written. When projects or capabilities are added, the command proposes
only new classifications and starter artifacts.

| Observed state | Required behavior |
|---|---|
| Expected file absent | Propose create |
| Existing file equals expected content | Retain |
| Managed file still equals its applied hash | Propose safe template update |
| Managed file differs from its applied hash | Preserve and report a merge collision |
| Reviewed current file accepted with `--accept-current` | Retain content and record `ownership: human` |
| Human-owned artifact on a later run | Retain regardless of starter-template changes |
| New component | Add classification and newly applicable starters |
| Missing component | Retain files and mark potentially stale by default; with explicit `--quarantine-obsolete`, move only unchanged CIS-managed artifacts to the recoverable quarantine tree |
| Unselected managed artifact already absent | Remove its stale manifest entry without deleting content |
| Existing catalog identity | Merge by stable ID or report identity/path conflict |
| No delta | Return `unchanged` |

Plain `repo init` performs reconciliation; a separate refresh command is not required.
Default initialization never removes obsolete content. The explicit, reviewed
`--quarantine-obsolete` operation moves an obsolete artifact to
`.cis/quarantine/repository-init/<original-path>` only when it is still managed and
its current hash equals the manifest's applied hash. Human-owned or edited content is
retained. Quarantine destinations must remain inside the repository and must not
already exist. The move is reported in every structured output and the manifest entry
is removed only as part of the confirmed plan.

## 8. Confirmation rules

Core empty-root scaffolding may be created directly. Any classification-selected
starter assets, managed-file updates, or changes to a non-empty documentation root
require review and confirmation.

```bash
cis repo init --repo . --root docs/cis --dry-run
cis repo init --repo . --root docs/cis --yes
cis repo init --repo . --root docs/cis --accept-current --yes
cis repo init --repo . --root docs/cis --quarantine-obsolete --dry-run
cis repo init --repo . --root docs/cis --quarantine-obsolete --yes
```

Classification overrides operate at component, role, framework, or capability level,
not by selecting individual documents. Override syntax is deferred until the
classification result contract is stable.

## 9. Safety invariants

- Never overwrite a user-edited managed artifact.
- Accepting current content changes manifest ownership only; it never rewrites the accepted file.
- Never delete an artifact because a classifier no longer selects it. Explicit quarantine is recoverable and is limited to unchanged CIS-managed files.
- Never execute target-repository assemblies.
- Never treat low-confidence inference as confirmed authority.
- Always expose classification, evidence, binding reasons, and artifact changes in
  human, JSON, and agent output.
- Catalog merges use stable identities and stop on conflicting identity/path pairs.
- Reapplying an identical plan is idempotent.

## 10. First implementation boundary

The first implementation delivers C#/.NET, Angular, Next.js/React, Swift/Apple, and
Kotlin/Android classification; foundational product, technical, context, and
assurance specifications; feature and ADR templates; a compact PARR-informed skill
set; repository-wide and path-specific instructions; relevant reference families;
deterministic best-effort reference seeding; repository profile generation;
starter-manifest persistence; additive reruns; managed-content collision detection;
and structured plan output.

Broader language classifiers, classification overrides, AI-assisted ambiguity review,
versioned template migration, deeper semantic extractors, row-level canonical merge
proposals, runtime agent-tool configuration, and explicit pruning remain later slices.

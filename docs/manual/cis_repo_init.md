---
title: "cis repo init"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-15"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-repo-init
---

# `cis repo init`

Classifies a target repository and initializes or reconciles its CIS documentation workspace. The command is designed to be rerun as the repository changes.

## Synopsis

```text
cis repo init --root <repository-relative-path> [options]
```

## Options

| Option | Required | Default | Effect |
| --- | --- | --- | --- |
| `--root <path>` | Yes | — | Sets the documentation root relative to the target repository, for example `docs/cis`. It must remain inside the repository and cannot be the repository root. |
| `--repo <path>` | No | Current directory | Selects the target repository. The directory must already exist. |
| `--dry-run` | No | `false` | Produces the complete classification and change plan without writing files. It does not require `--yes`. |
| `--yes` | No | `false` | Confirms the displayed classification and artifact changes when review is required. |
| `--accept-current` | No | `false` | After manual review, records conflicting current starter files as human-owned canonical artifacts. Their content is retained and subsequent init runs do not replace it with starter templates. Use with `--yes` when applying. |
| `--details` | No | `false` | Include per-component, starter-selection, and retained-path records in agent output. The default agent response is a compact summary plus actionable changes and findings. |
| `--quarantine-obsolete` | No | `false` | Moves obsolete CIS-managed artifacts whose content still matches the recorded applied hash to `.cis/quarantine/repository-init/<original-path>`. Edited or human-owned artifacts are retained. Preview with `--dry-run` and apply with `--yes`. |
| `--format <format>` | No | `human` | Selects `human`, `json`, or `agent` output. Values are case-insensitive. |
| `-?`, `-h`, `--help` | No | — | Shows command help and exits without initializing the repository. |

## Operation and effects

The command performs these steps in order:

1. Resolves the repository and documentation-root paths and rejects missing, absolute, escaping, or structurally conflicting paths.
2. Scans repository evidence and classifies components by language, framework, role, capabilities, and confidence. Supported evidence includes .NET and ASP.NET Core, JavaScript frontends such as Angular, Next.js, React, and Vue, native Swift and Kotlin products, Terraform, messaging, and persistence technologies.
3. Adds the always-seeded CIS baseline: repository, change-delivery, and business-requirements guidance;
   curated workflow skills including bounded planning and design review; product-intent,
   technical-intent, system-context, delivery-and-assurance, public-endpoint caching,
   and populated design-guideline
   specifications; feature, ADR, and design-guideline templates; and the repository profile.
4. Selects a curated PARR-derived standards baseline from repository classification. Every repository receives agent-documentation compliance; application, API, persistence, eventing, frontend, accessibility, observability, and edge-header standards are added only when their roles or capabilities apply. Product-specific PARR names, screens, routes, database choices, and status vocabularies are not copied.
5. Selects classification-bound implementation skill packs for .NET quality, API integration, persistence, frontend delivery, browser assurance, security, infrastructure, operations, dependency upgrades, and release work. The selected packs and evidence are recorded in `<root>/references/implementation-skill-packs.md`; only applicable implementation skills are seeded.
6. Selects classification-bound instructions and specification/reference pairs for APIs, commands, events, workflow states, business invariants, projections, permissions, configuration, packages, problem details, module ownership, data dictionaries, ERDs, screen routes, and traceability.
7. Seeds Draft references from deterministic repository evidence where supported, without executing repository code. Current extraction covers minimal and Next.js API routes; C# command, event, workflow-state, projection, exception, authorization, permission, and `DbSet<T>` declarations; application settings and environment examples; common package manifests; Next.js and Angular routes; and classified component ownership and traceability anchors. Likely secret configuration values are redacted.
8. Plans standard directories and CIS-owned state, merges required entries into the documentation catalog, and compares managed artifacts with the previous generated baseline.
9. Optionally plans recoverable quarantine moves for obsolete, unchanged CIS-managed artifacts. Human-owned or edited artifacts are never moved.
10. Reports all creates, updates, quarantine moves, retained paths, warnings, and collisions. When review is required, it stops with exit code `3` unless `--yes` or `--dry-run` was supplied.
11. Applies the plan only after all validation, collision, and confirmation checks pass.

The standard documentation structure contains:

```text
<root>/
  README.md
  catalog.yml
  architecture/decisions/
  specs/
  references/
  changes/
  templates/
```

The repository state contains:

```text
.cis/
  repository.yml
  starter-manifest.yml
  .gitignore
  local/init/scan.json
```

`.cis/repository.yml` records the configured documentation root. `.cis/starter-manifest.yml` records generated-artifact baselines for later reconciliation. `.cis/local/init/scan.json` is derived scan output, and `.cis/.gitignore` excludes `local/`, `cache/`, and `runs/` from source control.

The always-seeded agent assets are:

```text
.github/instructions/cis-repository.instructions.md
.github/instructions/cis-change-delivery.instructions.md
.github/instructions/cis-business-requirements.instructions.md
.github/skills/cis-change-impact/SKILL.md
.github/skills/cis-repository-bootstrap/SKILL.md
.github/skills/cis-skill-governance/SKILL.md
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
.github/skills/cis-design-review/SKILL.md
```

The always-seeded specifications include `<root>/specs/product-intent-spec.md`,
`technical-intent-spec.md`, `system-context-spec.md`, `delivery-and-assurance-spec.md`,
`repository-delivery-policy-spec.md`, `public-endpoint-caching-policy-spec.md`, and a
populated `<root>/specs/design-guidelines.md`.
The delivery policy starts Draft and enforces a safe local-only default until a
maintainer explicitly defines branch, commit, push, pull-request, merge, release, and
remote-issue authority. Reusable templates include feature,
ADR, and product-specific design guidelines. A copied feature template must use
`type: feature-specification`, allowing `cis brd reconcile` to absorb it through governed
source assessment and traceability. Classification-derived specifications are deliberately
Draft and combine detected facts with visible `TODO` sections requiring maintainer input.
References populated from source evidence also remain Draft until verified.

The PARR-derived starter standards are deliberately smaller than their source documents. They retain source-path provenance and the reusable rules, while removing PARR product assumptions. Selection is additive:

| Classification evidence | Seeded standard |
|---|---|
| Every repository | Agent documentation compliance |
| Application behavior | Testing, secure feature implementation, observability |
| Backend API producer | HTTP API boundary, edge security headers |
| Persistence capability | Repository and transaction boundaries |
| Event producer or consumer | Outbox and projection behavior |
| Web or native frontend | Frontend interactions/components and accessibility/responsive regression |
| HTTP API or web frontend | Edge security headers |

Every seeded rule is added to the standards conformance matrix with `manual-review` enforcement until the target repository supplies a stronger deterministic check. Initialization preserves reviewed edits and never removes a standard merely because later classification no longer selects it.

After applying initialization, run `cis skills validate --strict`, followed by `cis skills audit`. The validator checks the portable metadata, structure, and referenced-resource contract. Audit combines deterministic checks with local-first, configured-remote-fallback model review. It remains read-only unless explicit `--fix` authorizes reversible quarantine.

Initialization also seeds the canonical
`<root>/references/task-type-capability-selections.md`. It begins with no explicit
extension selection. Loaded providers with mutually exclusive applicable capability
types cause planning to stop until a human records a selection through
`cis plan capability select`; repository initialization never selects a provider.

For each classified UI-bearing component, init first detects an existing component
framework from dependencies, configuration, imports, and source markers. Existing
frameworks are preserved. If none is evidenced, init records a classification-bound
default in `<root>/references/ui-framework-profile.md` and creates a component-scoped
UI instruction under `.github/instructions/`. The defaults are shadcn/ui with Tailwind
CSS for React/Next.js, Angular Material for Angular, Nuxt UI for Vue/Nuxt, SwiftUI for
Apple native, Jetpack Compose Material 3 for Android native, and Godot Control/Theme
resources for Godot. Motion is conditional rather than mandatory. Init records these
choices but never installs or migrates UI packages.

## Idempotency and existing content

- An identical rerun reports `unchanged` and does not rewrite files.
- Newly detected projects or capabilities add their newly selected artifacts and update managed indexes or profiles.
- A rerun refreshes deterministic reference seeds only while their managed baseline has not been human-edited.
- Existing human-authored files are retained when CIS does not own them or no generated update is required.
- A human edit that overlaps a required generated update is reported as a collision for manual merging.
- After that review, `--accept-current --yes` resolves starter collisions by retaining the current files and recording `ownership: human` in `.cis/starter-manifest.yml`; it never overwrites their content.
- Previously managed artifacts that are no longer selected are retained and reported; initialization does not delete them.
- With explicit `--quarantine-obsolete`, an obsolete artifact is moved only when it remains CIS-managed and its content hash equals the last applied hash. The original repository-relative structure is preserved beneath `.cis/quarantine/repository-init/` for inspection and restoration.
- Edited, human-owned, directory-valued, escaping, or destination-colliding obsolete artifacts are retained or rejected; they are never overwritten or deleted.
- A manifest entry for an artifact that is both unselected and already absent is pruned as stale state; no content is deleted.
- Catalog entries are merged by stable ID and path rather than replacing the catalog.
- File contents are checked again before an update is written, preventing a plan from silently overwriting a concurrent edit.

## Confirmation behavior

Confirmation is required when a non-dry-run plan has changes and at least one of these conditions applies:

- the documentation root already contains content;
- classification selected starter artifacts; or
- an existing file must be updated.

Run first with `--dry-run` to inspect the plan, then repeat with `--yes` to apply it. `--yes` does not override validation errors or collisions. For a reviewed collision whose current file is the intended canonical version, rerun with `--accept-current --yes`.

If init returns exit code `2`, exit code `4`, an error, or a collision, run doctor with the same repository and root:

```powershell
cis repo doctor --repo C:\work\orders --root docs/cis --format agent
```

Exit code `3` is a confirmation gate rather than an initialization failure; review the plan before using `--yes`.

## Output

Human output summarizes the classification, selected starters, planned file operations, warnings, and errors. JSON emits the complete result with camel-case property names. Agent output emits a summary followed by line-oriented records such as `component=`, `starter=`, `createFile=`, `updateFile=`, `quarantine=`, `retain=`, `warning=`, `collision=`, and `error=`.

The structured result includes the status, resolved paths, classification, starter selections, directories and files to create, files to update, quarantine moves, retained paths, warnings, collisions, errors, confirmation state, application state, and exit code.

## Status and exit codes

Common statuses are `dry-run`, `confirmation-required`, `collision`, `unchanged`, and `initialized`.

| Code | Meaning |
| --- | --- |
| `0` | Initialization was applied, the repository was already unchanged, or a dry run completed successfully. |
| `2` | An input, path, repository, or output format is invalid. |
| `3` | The plan is valid but requires explicit confirmation. No files were changed. |
| `4` | A required path or managed-content collision prevents application. No files were changed. |

## Examples

Preview initialization of the current repository using `docs/cis` as its documentation root:

```powershell
cis repo init --root docs/cis --dry-run
```

Apply the reviewed plan:

```powershell
cis repo init --root docs/cis --yes
```

Retain reviewed conflicting files as human-owned canonical artifacts:

```powershell
cis repo init --root docs/cis --accept-current --yes
```

Preview and then apply recoverable quarantine of unchanged obsolete managed artifacts:

```powershell
cis repo init --root docs/cis --quarantine-obsolete --dry-run
cis repo init --root docs/cis --quarantine-obsolete --yes
```

Initialize another repository and return agent-oriented output:

```powershell
cis repo init --repo C:\work\orders --root engineering-docs --yes --format agent
```

## Related commands

- [`cis repo import`](cis_repo_import.md)
- [`cis repo doctor`](cis_repo_doctor.md)
- [`cis docs inventory`](cis_docs_inventory.md)
- [`cis docs validate`](cis_docs_validate.md)

---
title: "Implementation Skill Packs and Validation Specification"
type: specification
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-15"
review_cadence: "on classification or skill contract change"
cis:
  stable_id: change-impact-studio:spec:implementation-skill-packs-validation
---

# Implementation skill packs and validation specification

## Purpose

CIS must give an agent the implementation guidance appropriate to the repository it is changing without seeding every ecosystem-specific skill into every repository. `cis repo init` therefore selects named implementation skill packs from deterministic repository classification. Markdown remains canonical and reviewable; classification scan state remains derived.

## Selection contract

Selection uses the component languages, frameworks, roles, capabilities, and source evidence already produced by repository classification. It does not execute target assemblies, install dependencies, or invoke a model.

| Pack | Selection signal | Seeded implementation skills |
| --- | --- | --- |
| `dotnet-quality` | C# language | targeted .NET tests, unit tests, architecture tests, mutation tests |
| `api-integration` | backend API producer | API integration tests |
| `data-persistence` | persistence capability or database role | migrations, real-dependency integration tests, schema/SQL review |
| `frontend-delivery` | web, mobile, native, or Godot frontend | frontend implementation, accessibility review, and frontend observability |
| `browser-assurance` | Angular, Next.js, React, Vue, or Nuxt frontend | browser regression tests |
| `secure-delivery` | authorization capability or backend API producer | secure feature delivery |
| `infrastructure-delivery` | infrastructure role or Terraform | infrastructure delivery |
| `operational-readiness` | backend API, worker, event producer, or event consumer | observability implementation |
| `dependency-and-release` | package, deployment, or package-producer evidence | dependency upgrades and release rollout |

Each selected pack creates a starter selection with its classification evidence. Skills are deduplicated by path when two applicable packs share one skill. Core CIS workflow skills remain always seeded, including `cis-skill-governance`, which directs agents through inventory, strict validation, safe repair, reviewed import, and non-destructive audit.

Initialization writes `<documentation-root>/references/implementation-skill-packs.md`. The profile records selected packs, seeded skill names, evidence, and the reconciliation command. It is cataloged as canonical Markdown. When classification changes, init plans additions or managed updates; it never silently deletes a previously selected human-managed skill.

## Portable skill contract

Each immediate child of `.github/skills/` is one skill and must contain `SKILL.md`.

- The directory and front-matter `name` must match exactly.
- Names contain lowercase letters, digits, and single hyphen separators and are at most 64 characters.
- YAML front matter requires scalar `name` and `description` fields.
- `description` is non-empty and at most 1024 characters.
- The instruction body is non-empty.
- The instruction body contains a level-one Markdown heading.
- Relative Markdown resource links resolve inside the repository.
- Duplicate declared names are errors.
- Front-matter fields outside the portable `name` and `description` contract are warnings.
- A `SKILL.md` over 500 lines is a warning to move detail into referenced resources.

The validator intentionally does not infer prose quality or mandate one trigger phrase. Those remain review concerns because equivalent natural-language descriptions can express applicability in several forms.

## Commands and exits

`cis skills inventory` lists skills and fails only for structural or metadata errors. `cis skills validate` reports all diagnostics; `--strict` also fails on warnings. Explicit `--fix` applies only deterministic missing-structure repairs before revalidation: absent front matter, absent `name` or `description`, an empty body, and an absent level-one heading. It preserves existing body text and optional metadata. Malformed YAML, conflicting names, duplicates, unsupported fields, large files, and broken links remain manual diagnostics. Both commands support human, JSON, and stable line-oriented agent output.

`cis skills audit` reviews the valid installed inventory for exact duplicates, overlapping responsibility, and conflicting instructions. Exact normalized instruction bodies and identical normalized descriptions are checked deterministically. A bounded set of likely-related pairs is then submitted one pair per prompt to an available model unless `--no-llm` is set. Provider selection prefers local generation and falls back to an available explicitly configured remote provider. A requested model narrows provider selection when available. Remote fallback transmits each candidate's name, description, and at most 1,500 body characters; `--no-llm` is the local-only deterministic path. A conflict requires mutually exclusive instructions applicable to the same situation; complementary scopes are not conflicts. A model conflict must supply separate evidence of a required directive and a prohibition or it is omitted with a warning. Model output is constrained to supplied pairs and retained as advisory evidence with method and confidence. Missing, failed, or malformed model output produces warnings while preserving deterministic findings.

Without `--fix`, audit isolation is non-destructive and replaces only derived `.cis/local/skills/audit.json` and `audit.md`. Model classifications, including possible conflicts, are reviewable warnings because the model is not approval authority. They fail under `--strict`.

`--fix` explicitly authorizes reversible filesystem quarantine. Deterministic exact-duplicate groups keep their alphabetically first active skill and move redundant copies. Evidence-backed conflicts move both involved bundles because CIS cannot choose a winner. Overlaps never move automatically. Moves target `.github/skills-quarantine/<name>/`, preflight every destination, never overwrite existing quarantine content, and attempt rollback after a partial failure. Quarantined bundles retain all files for manual review and restoration and are excluded from active skill/import discovery, repository classification, graph extraction, and routing-card indexing.

Exit code `0` means the selected validation policy passed. Exit code `2` means configuration, format, skill structure, metadata, link validation, or strict warning policy failed.

## Doctor integration

The skills module contributes its non-strict diagnostics to `cis repo doctor`. Doctor stays read-only and suggests `cis skills validate --strict`; it never rewrites a skill or accepts a warning automatically.

## Existing and remote skill import

`cis skills import` brings complete preexisting bundles into `.github/skills/<name>/` from local directories, local ZIP files, GitHub repository or tree URLs, and direct HTTP(S) ZIP URLs. Remote content is downloaded into isolated staging, bounded by archive and bundle limits, checked for path traversal and links, and validated through the same portable contract. `--fix` changes staged copies only.

Import requires dry-run review and explicit confirmation for creates. Same-name, same-hash bundles are unchanged; same-name, different-hash bundles are conflicts and are never overwritten. Successful import rebuilds the disposable routing index at `.cis/local/skills/index.json`. Scripts and other bundled resources are copied but never executed.

## Acceptance criteria

- .NET, API, persistence, frontend, security, infrastructure, operations, and release classifications select only their applicable packs.
- Initialization visibly records pack evidence and is unchanged on an identical rerun.
- Generated skills conform to the portable contract.
- Missing files, invalid or mismatched names, malformed metadata, duplicates, empty bodies, and broken local links fail validation.
- `--fix` repairs missing portable YAML fields and headings, reports changed paths, and is a no-op for already valid files.
- Optional metadata and oversized skills remain visible warnings and fail under `--strict`.
- Repository doctor surfaces skill diagnostics through the shared doctor extension contract.
- Local and GitHub/ZIP imports preserve complete bundles, enforce confirmation and conflicts, and produce an idempotent derived routing index.
- Audit deterministically identifies exact duplicate bodies, bounds local-first semantic review, rejects unrequested model pairs, and persists reviewable derived findings without modifying skill bundles unless `--fix` explicitly quarantines eligible findings.
- Audit remains useful without any model, prefers local generation, and uses an available configured remote provider unless `--no-llm` selects deterministic-only review.
- Explicit `--fix` quarantines redundant deterministic duplicates and both sides of evidence-backed conflicts without deleting or overwriting bundles; overlaps remain active.

---
title: "Reference Governance and Drift Specification"
type: specification
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-08"
review_cadence: "on reference-provider contract change"
cis:
  stable_id: change-impact-studio:spec:reference-governance-drift
---

# Reference Governance and Drift Specification

## Purpose

Canonical Markdown reference tables describe durable engineering contracts. CIS extracts
deterministic source observations, correlates them with those tables, and reports drift;
derived observations never modify or replace the canonical documents.

## Provider contract

`ICisReferenceProvider` registers one stable reference kind, canonical filename,
description, supported source paths, and deterministic per-file extraction. Duplicate
kind registrations are conflicts and fail validation. Provider load order never chooses
a winner.

The built-in providers cover configuration, permissions, commands, events, workflow
states, business invariants, projections, Problem Details, packages, screen routes,
module ownership, persistent entities, and entity relationships. The specialized
`api` module continues to own HTTP/OpenAPI correlation and compatibility.

## Canonical and derived state

Reference Markdown under the configured `references/` directory is canonical. Stable
identities, lifecycle, evidence, and human rationale remain there. Normalized state is
written to `.cis/local/references/inventory.json`; it contains provider provenance,
repository revision, source locations, aliases, canonical correlation, diagnostics,
and a content digest. Repeated unchanged discovery is idempotent.

Inventory schema 2 includes each canonical row's repository scope and complete identity
key. Keys retain dimension boundaries and punctuation; loose source-correlation aliases
are not duplicate-detection keys. Default validation replaces obsolete derived state;
cached reads reject older schemas with a discovery instruction.

| Family | Identity within a repository |
| --- | --- |
| Data | Entity, field |
| ERD | Entity, relationship, target |
| Workflow state | Workflow ID (or workflow), state |
| Configuration | Owner, path (or name when no path column exists) |
| Package | Package, component |
| Screen/route | Route, component, platform |
| Other families | Stable ID/code |

An explicit `Repository` value must resolve to a product-owned repository in the workspace.
Absent repository columns retain local-repository semantics. Evidence resolves under that
owner's root, with path containment and reparse-point checks. Provider observations remain
local to the command's selected repository and cannot correlate to another owner's row.
Current foreign rows report an unresolved source-correlation warning rather than claiming
their implementation was inspected or incorrectly asserting their source is missing.
Malformed table rows are reported; escaped literal pipes remain part of a cell.

### Source-document projections

Binary and narrative BRD evidence is registered in canonical
`<documentation-root>/references/source-evidence.md`. Each entry has a stable
`BRD-SRC-*` identity derived from repository identity and source path, a reviewed role,
actor, rationale, and accepted source digest. The original `.docx`, Markdown, text file,
or initialized repository remains authoritative.

CIS writes disposable projections beneath `.cis/local/references/<BRD-SRC-ID>/`:

- `content.md` contains readable text with stable paragraph anchors;
- `source-map.json` maps anchors to Word parts and paragraph identities;
- `index-card.md` provides a small deterministic routing summary;
- `diff.json` identifies anchor changes and whether a cited BRD anchor changed;
- `manifest.json` records accepted and detected digests and extractor provenance.

Rebuilding never rewrites the BRD. A changed digest remains a review candidate until
explicitly accepted; BRD currency is lost only when a cited anchor changed or
disappeared, or when the BRD used an unbounded source-level citation.

An initialized repository can be selected when it is the authority repository or a
registered workspace participant and has a fresh context graph. Its bounded projection
contains classifications, components, contracts, behavior, relationships, and source
routes with stable node anchors. CIS does not concatenate the source tree. The graph
build identity is the accepted repository snapshot digest.

## Commands

- `cis references discover` extracts and correlates source identities.
- `cis references inventory` reads normalized state without rescanning.
- `cis references validate` checks provider conflicts, duplicate identities, source and
  canonical drift, and evidence paths. `--strict` gates warnings.
- `cis references diff --base <revision>` compares canonical rows and source identities
  with an exact Git baseline and reports source changes without a reference co-change.
- `cis references source import` registers a repository-owned source and builds its projection.
- `cis references source build` refreshes projections without advancing the registered digest.
- `cis references source status` reports digest and material-citation drift read-only.
- `cis references source accept` records acceptance of a reviewed source revision.

All commands support human, JSON, and compact agent output. Repository configuration
errors use exit code 2; unavailable state uses 4; governance failures use 5.

## Safety and evidence rules

- Discovery never writes canonical Markdown.
- Generated, dependency, build, coverage, Git, and `.cis/local/` paths are excluded.
- Source observations must retain repository-relative path and line when available.
- Missing source evidence does not delete a canonical row; lifecycle requires review.
- A source observation cannot prove a business rule, permission meaning, compatibility,
  or ownership decision by itself.
- Git comparison uses argument-list process execution without a shell.
- Source documents must be repository-owned `.docx`, `.md`, or `.txt` files. Repository
  sources must be initialized, workspace-registered when external, and freshly graphed.
  Linked, unregistered, secret-shaped, oversized, stale, and malformed inputs are rejected.
- `cis agent author brd` registers selected repository-owned references and reconciles
  the managed BRD source table before agent work.

## Completion criteria

The module is complete when every seeded non-API reference family has a registered
provider or an explicit specialized owner, conflicts are persisted as errors, strict
validation detects unmatched identities and invalid evidence, baseline diff detects
canonical and source-only changes, repository doctor routes missing state, and tests
cover registration, idempotence, correlation, drift, and Git-baseline behavior.

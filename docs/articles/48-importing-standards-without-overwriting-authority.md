---
title: "Importing Standards Without Overwriting Local Authority"
type: article
status: Active
series: "Standards and Engineering Policy"
series_order: 7
owner: "Andrew Spiteri"
last_reviewed: "2026-09-10"
review_cadence: on standards-import change
summary: "Stage, validate, preview, reconcile, and confirm external standards while preserving divergent repository content."
cis:
  stable_id: change-impact-studio:article:import-standards-preserve-authority
---

# Importing standards without overwriting local authority

Organizations often want to reuse engineering standards across repositories. A direct
copy can overwrite local rules, duplicate identities, or import product assumptions
that do not apply.

## Import into staging

CIS accepts bounded local, archive, or supported remote sources into a staging area.
Structural validation occurs before canonical admission.

## Preview identity and content conflicts

The import compares stable IDs, paths, normalized rules, catalog entries, and conformance
mappings. Divergent existing content becomes a collision for review, not last-writer-wins.

## Confirm canonical admission

A dry run shows additions and conflicts. Explicit confirmation admits reviewed content.
Model duplicate or overlap findings remain advisory.

## Preserve provenance

Imported standards record their source while becoming repository-owned canonical files.
Source provenance does not transfer approval from another repository automatically.

## Use reversible quarantine for audit repair

When explicitly authorized, audit repair may quarantine conflicting candidates while
preserving historical catalog and conformance evidence. It does not merge or delete
meaning automatically.

## Source authority does not transfer automatically

An organization may maintain an approved standard in a central repository. The importing
repository still needs to decide whether that standard applies to its product, how it
interacts with local rules, and who owns future review. Provenance establishes origin;
local admission establishes repository authority.

This distinction is essential when a central standard contains assumptions about stacks,
deployment, data, or risk that do not match the target repository.

## Use a staged admission pipeline

```text
Acquire bounded source
  → verify archive and path safety
  → parse metadata and stable rule IDs
  → compare paths, identities, and normalized content
  → validate catalog and conformance effects
  → preview additions, matches, and collisions
  → obtain confirmation
  → admit canonical files atomically
```

No stage should write over divergent reviewed content. A failed validation leaves the
canonical repository unchanged and preserves diagnostics for review.

## Distinguish match from equivalence

The same rule ID and same normalized content can be an unchanged match. The same ID with
different normative text is a collision. Similar wording under different IDs may be a
duplicate or an intentional local specialization; a model can flag it, but a human must
decide.

Path equality alone is weak evidence. Two standards can use the same filename and carry
different authority. Stable identity and content comparison make the conflict explicit.

## Reconcile source updates deliberately

A later import may add rules, clarify text, deprecate obligations, or change enforcement
mappings. Review the delta against local exceptions, tasks, templates, and conformance
evidence. Do not treat “newer upstream” as permission to overwrite local changes.

Where the repository intentionally diverges, preserve the local standard and its source
relationship. Where it adopts the update, record the new provenance and lifecycle through
an inspectable diff.

## Quarantine is not deletion

Audit repair may move conflicting or obsolete generated candidates into a reversible
location after explicit authorization. It should preserve identity, source, reason, and
catalog or conformance implications. Quarantine gives maintainers space to reconcile; it
does not decide which meaning wins.

Human-authored canonical content should never be quarantined merely because a fresh import
would prefer another version.

## Treat archives and remote sources as untrusted input

Validate containment, size, file type, checksums where available, and extraction paths.
Reject path traversal and unsupported content before staging. Credentials remain outside
repository files, and remote acquisition does not authorize unrelated network access.

## Verify the admitted result

After confirmation, run strict documentation and standards validation, inspect the
catalog, resolve applicability for representative targets, and review conformance gaps.
An import that writes valid files but leaves duplicate rules, broken mappings, or an
unexpected Active lifecycle is not complete. Repeating the same import should be
idempotent and report unchanged content.

## Takeaway

Reuse standards through staged validation and human reconciliation. Preserve local
authority, stable identity, source provenance, and reversible history.

## Canonical CIS sources

- [Standards governance](../specs/standards-governance-spec.md)
- [`cis standards import`](../manual/cis_standards_import.md)
- [`cis standards audit`](../manual/cis_standards_audit.md)

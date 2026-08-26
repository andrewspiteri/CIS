---
title: "Importing Standards Without Overwriting Local Authority"
type: article
status: Draft
series: "Standards and Engineering Policy"
series_order: 7
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
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

## Takeaway

Reuse standards through staged validation and human reconciliation. Preserve local
authority, stable identity, source provenance, and reversible history.

## Canonical CIS sources

- [Standards governance](../specs/standards-governance-spec.md)
- [`cis standards import`](../manual/cis_standards_import.md)
- [`cis standards audit`](../manual/cis_standards_audit.md)


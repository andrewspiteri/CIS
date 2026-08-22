---
title: "cis brd init"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-brd-init
---

# `cis brd init`

Creates or reconciles the canonical business requirements document in the workspace
authority repository. The result is never automatically Active.

## Synopsis

```text
cis brd init [--workspace <path>] [--title <text>]
  [--format <human|json|agent>]
```

When candidates exist, CIS records their repository, path, content hash, and an
`Unreviewed` assessment row. When none exist, it records that no source BRD was found.
In both cases it creates the same required human-review sections and status
`Review Required`.

Reconciliation updates participant graph baselines and candidate hashes while
preserving completed sections and assessments whose evidence hash has not changed.
New or changed evidence resets the affected assessment to `Unreviewed`. Any material
reconciliation clears prior approval. An unmanaged document already occupying the
canonical path is a collision and is never overwritten.

The canonical path is `<authority-root>/specs/business-requirements.md`; it is added to
the authority repository's documentation catalog.

## Human editing boundary

Humans assess each source as `Adopted`, `Reference`, or `Rejected`, record rationale,
and complete all required sections. Candidate IDs, hashes, baseline rows, and managed
block markers remain tool-managed.

## Exit codes

| Code | Meaning |
| ---: | --- |
| `0` | Draft created, reconciled, or unchanged. |
| `2` | Workspace, title, configuration, or evidence is invalid. |
| `4` | Canonical path or catalog identity collides. |

## Example

```powershell
cis brd init --workspace C:\work\product-docs --title "Commerce Business Requirements"
```

## Related commands

- [`cis brd discover`](cis_brd_discover.md)
- [`cis brd validate`](cis_brd_validate.md)
- [`cis brd status`](cis_brd_status.md)

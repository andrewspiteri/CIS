---
title: "Safe Path Handling and Atomic Repository Mutation"
type: article
status: Draft
series: "Building Change Impact Studio"
series_order: 7
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: on repository-mutation change
summary: "Resolve containment, preview complete plans, detect collisions, confirm intent, and apply only after all validation succeeds."
cis:
  stable_id: change-impact-studio:article:safe-paths-atomic-mutation
---

# Safe path handling and atomic repository mutation

A repository tool can create many files quickly. Safety depends on proving exactly where
those files go and what they replace before writing the first one.

## Resolve every path

CIS resolves repository, documentation root, source, output, template, and changed-file
paths to absolute locations. Repository-relative inputs that escape the allowed root,
collide structurally, or target the repository root where prohibited are rejected.

## Plan before applying

Initialization computes directories, creates, updates, retained paths, warnings, and
collisions for the complete operation. Dry run exposes the same plan without mutation.

## Treat concurrent edits as evidence

Before writing, CIS checks current content against the planned baseline. A file changed
after planning becomes a collision rather than being overwritten.

## Preserve human ownership

Applied starter hashes distinguish unchanged managed content from human edits. Reviewed
current content can be accepted as human-owned; future reconciliation will not replace it.

## Use recoverable quarantine

Obsolete unchanged managed artifacts can move to a repository-contained quarantine only
after explicit preview and confirmation. Edited or human-owned files remain in place.

## Apply atomically

Validation and collision checks complete before canonical mutation. Multi-file operations
replace state only when the complete plan is valid.

## Takeaway

Repository automation should be path-contained, previewable, idempotent, collision-aware,
ownership-preserving, recoverable, and atomic. Convenience never justifies silent overwrite.

## Canonical CIS sources

- [CLI and repository initialization](../specs/cli-and-repository-initialisation-spec.md)
- [Classification-driven initialization](../specs/classification-driven-initialisation-spec.md)
- [`cis repo init`](../manual/cis_repo_init.md)


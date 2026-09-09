---
title: "Safe Path Handling and Atomic Repository Mutation"
type: article
status: Draft
series: "Building Change Impact Studio"
series_order: 7
owner: "Andrew Spiteri"
last_reviewed: "2026-09-09"
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

The same primitives now protect agent worktrees and changed paths, local-artifact archives,
reference preparation and recovery copies, MCP operations, and VS Code file navigation.
Each surface narrows its own permitted root instead of assuming that “inside the repository”
is sufficient authority.

## Paths are an authority boundary

A relative path is not safe merely because it lacks `..`. Separators, rooted segments,
case behavior, symlinks or reparse points, normalization, and platform rules can change
where it resolves. CIS should calculate the final absolute path and prove containment in
the specific permitted root before reading or writing.

Different operations have different roots. An authority-document edit, participant
source change, artifact archive, and VS Code navigation should not all inherit permission
to touch any path below a broad workspace directory.

## Separate planning from mutation

A mutation pipeline should:

1. resolve repositories and allowed roots;
2. classify current inputs and ownership;
3. calculate every directory, create, update, retain, quarantine, and catalog effect;
4. validate identities, collisions, and cross-file invariants;
5. present the complete dry-run plan;
6. obtain explicit confirmation where required;
7. recheck planned baselines for concurrent edits; and
8. apply or roll back the complete transaction.

This sequence prevents the first file from being written before a collision in the last
file is known.

## Not every difference is an update

An unchanged managed starter can be reconciled safely. A human-edited file is retained or
requires explicit ownership acceptance. A path with the right content but a conflicting
stable ID is an identity collision. A catalog entry pointing elsewhere is a routing
collision. These cases need different diagnostics and recovery.

Last-writer-wins would destroy the evidence needed to tell them apart.

## Atomicity needs recovery evidence

Where a true filesystem transaction is unavailable, CIS can stage content, validate it,
replace files in a controlled order, and keep rollback material inside a contained
temporary boundary. A failure must report what changed and whether rollback completed.

Multi-repository operations are planned as a batch and rejected before mutation when a
source is invalid. They must not leave some participants initialized and a workspace
registry claiming the entire import succeeded.

## Concurrency is normal

Editors, formatters, agents, and other CIS controllers may change files after dry run.
Before apply, compare current content or digest with the planned baseline. A mismatch
returns to review; confirmation of the earlier plan is not authorization to overwrite a
new edit.

## Quarantine stays reversible

Only eligible unchanged managed artifacts move, only inside the repository's quarantine,
and only after preview and confirmation. Record original path, identity, reason, and
content digest. Human-owned content remains in place unless a separate explicit action
governs it.

## Make safety visible in output

Dry-run and apply results should name the resolved repository and documentation roots,
list bounded operations, expose omitted counts, and distinguish retained, collision, and
error states. A user cannot review a confirmation prompt that hides the final paths.

Tests should cover platform separators, case behavior, traversal, root targeting,
reparse-point or symlink escapes, duplicate normalized paths, concurrent edits, and
rollback failure. Path safety is a behavioral contract, not a helper-method detail.

## Takeaway

Repository automation should be path-contained, previewable, idempotent, collision-aware,
ownership-preserving, recoverable, and atomic. Convenience never justifies silent overwrite.

## Canonical CIS sources

- [CLI and repository initialization](../specs/cli-and-repository-initialisation-spec.md)
- [Classification-driven initialization](../specs/classification-driven-initialisation-spec.md)
- [`cis repo init`](../manual/cis_repo_init.md)

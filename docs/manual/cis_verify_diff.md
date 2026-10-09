---
title: "cis verify diff"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-14"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-verify-diff
---

# `cis verify diff`

Capture changed files across the product-owned workspace repositories. External dependencies are outside delivery verification scope.

```text
cis verify diff <change-id> [--repo <path>] [--format <human|json|agent>]
```

New Git-backed dossiers preserve both the Git commit and a content-digested snapshot of the dirty
working tree for each owned repository. Verification therefore reports only work
performed after change creation: an unchanged pre-existing tracked or untracked file is
excluded, while a later edit, deletion, restoration, or new file is reported. Legacy
dossiers use the authority baseline and report an explicit participant-HEAD fallback
warning. The schema-3 snapshot records repository-qualified files, content digests,
baseline provenance, and a deterministic inventory digest under `.cis/local/verify/`.
Changing the content of an already-modified file therefore makes the snapshot stale;
changing only its Git status is not required to trigger the guard. The snapshot is
derived evidence, not acceptance.

For a repository without Git, new dossiers retain the graph build ID and a creation-time
inventory of repository file paths and SHA-256 hashes. Verification compares that retained
inventory with current files, including new and deleted paths. The inventory uses execution
input exclusions for disposable output and local CIS state, but includes canonical change
dossiers. Capture is bounded to 100,000 files and 128 MiB per file and rejects linked inputs.
An owned participant without Git needs a built graph before change creation.

A legacy graph build ID alone cannot establish the original file inventory. Such a dossier
fails with `CIS-VERIFY-GRAPH-BASELINE-MISSING`. Restore the original retained evidence or
explicitly authorize a new baseline or rehearsal. Rebuilding today's graph does not recover
the original baseline, and this command never manufactures that history.

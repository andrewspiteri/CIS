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

Capture tracked and untracked changed files across every registered workspace repository.

```text
cis verify diff <change-id> [--repo <path>] [--format <human|json|agent>]
```

New dossiers preserve both the Git commit and a content-digested snapshot of the dirty
working tree for every workspace repository. Verification therefore reports only work
performed after change creation: an unchanged pre-existing tracked or untracked file is
excluded, while a later edit, deletion, restoration, or new file is reported. Legacy
dossiers use the authority baseline and report an explicit participant-HEAD fallback
warning. The schema-3 snapshot records repository-qualified files, content digests,
baseline provenance, and a deterministic inventory digest under `.cis/local/verify/`.
Changing the content of an already-modified file therefore makes the snapshot stale;
changing only its Git status is not required to trigger the guard. The snapshot is
derived evidence, not acceptance.

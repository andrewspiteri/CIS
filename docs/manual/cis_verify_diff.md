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

New dossiers preserve a Git baseline per workspace repository. Legacy dossiers use the authority baseline and report an explicit participant-HEAD fallback warning. The schema-2 snapshot records repository-qualified files, baseline provenance, and a deterministic digest under `.cis/local/verify/`; it is derived evidence, not acceptance.

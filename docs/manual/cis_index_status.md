---
title: "cis index status"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-index-status
---

# `cis index status`

Reports indexed, fresh, stale, missing, and removed file-card coverage without
invoking a model.

Whole-repository status uses `.cis/local/index-cards/status.json`. The cache binds the
index manifest and every eligible source path to its length and last-write timestamp.
When metadata changes, CIS rechecks content hashes and refreshes the disposable cache;
therefore a timestamp-only change is reconciled as fresh instead of remaining falsely
stale. Path-scoped status always performs the bounded filesystem assessment.

```text
cis index status [--repo <path>] [--path <relative-file-or-directory>]
  [--format <human|json|agent>]
```

Exit `0` means the index was inspected, including a stale result; exit `2` means the
repository or selection is invalid; exit `4` means no index exists; exit `5` means the
stored index could not be assessed.

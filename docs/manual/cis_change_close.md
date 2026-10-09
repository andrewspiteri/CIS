---
title: "cis change close"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-10-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-change-close
---

# `cis change close`

Records explicit human closure in `proposal.md` and appends a lifecycle event.

```text
cis change close <change-id> [--repo <path>] [--format <human|json|agent>]
```

Repeating closure returns `unchanged`. This command records lifecycle state only; it
does not approve verification, commit files, or claim delivery completion.

Acceptance and closure write bookkeeping after an earlier diff capture. When invoking them separately, retain the accepted snapshot as evidence, then run `cis verify diff <change-id>` and `cis verify validate <change-id>` against the same repository after those writes. Confirm that any differences are expected; do not treat recapture as approval of new scope. Prefer [verify finalize](cis_verify_finalize.md) for a standard final-sweep/coordination plan.

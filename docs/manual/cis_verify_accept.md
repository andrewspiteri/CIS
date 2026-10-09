---
title: "cis verify accept"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-10-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-verify-accept
---

# `cis verify accept`

Record explicit human final verification acceptance.

```text
cis verify accept <change-id> --reviewer <identity> --reason <rationale> [--repo <path>]
```

Acceptance is blocked until deterministic validation passes.

This command records acceptance only. Prefer `cis verify finalize` when the generated plan contains the standard final-sweep and coordination lifecycle tasks.

Acceptance and closure write bookkeeping after an earlier diff capture. When invoking them separately, retain the accepted snapshot as evidence, then run `cis verify diff <change-id>` and `cis verify validate <change-id>` against the same repository after those writes. Confirm that any differences are expected; do not treat recapture as approval of new scope. Prefer [verify finalize](cis_verify_finalize.md) for a standard final-sweep/coordination plan.

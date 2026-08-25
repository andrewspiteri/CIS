---
title: "cis design approve"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-23"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-design-approve
---

# `cis design approve`

Approves the exact textual wireframe, renderer, and PNG manifest and releases the global
design barrier.

```text
cis design approve <change-id> --reviewer <identity> --reason <rationale> [--repo <path>] [--format <human|json|agent>]
```

Only a valid `PausedForReview` pack can be approved. CIS records reviewer, rationale,
wireframe digest, renderer hash, manifest hash, timestamp, and audit events. If the
wireframe was not separately approved, the same human decision approves its current
validated digest atomically. Agents must never infer or exercise this authority.

Do not request a duplicate approval for a provenance-only refresh. When the current
feature already carries exact human authority and the regenerated PNG manifest is
identical to an earlier approved manifest, use `cis design reconcile` instead. A visual
change or failed authority check still requires this explicit approval command.

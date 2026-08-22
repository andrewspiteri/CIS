---
title: "cis design approve"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-13"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-design-approve
---

# `cis design approve`

Approves the exact renderer and PNG manifest and releases the global design barrier.

```text
cis design approve <change-id> --reviewer <identity> --reason <rationale> [--repo <path>] [--format <human|json|agent>]
```

Only a valid `PausedForReview` pack can be approved. CIS records reviewer, rationale,
renderer hash, manifest hash, timestamp, and audit event. Agents must never infer or
exercise this authority.


---
title: "cis verify accept"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-14"
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

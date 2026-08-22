---
title: "cis technical-intent status"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-16"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-technical-intent-status
---

# `cis technical-intent status`

```text
cis technical-intent status [--workspace <path>] [--format <human|json|agent>]
```

Reports `Missing`, `Review Required`, `Ready for Approval`, `Active`, or `Stale` from
canonical content, BRD and participant baselines, decisions, and approval evidence.
An inactive or changed BRD and unavailable or stale participant graphs make an existing
technical intent non-current; they are reported as lifecycle warnings rather than as
structural command errors. Thus an otherwise valid approved document becomes `Stale`.
Status is read-only. `cis change create`, `cis plan build`, and `cis plan import-spec`
enforce Active/current readiness when run in a workspace authority.

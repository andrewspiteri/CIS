---
title: "cis brd status"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-brd-status
---

# `cis brd status`

Reports the canonical BRD's effective lifecycle using document state, approved-content
digest, participant baselines, current candidates, source assessments, and required content.

## Synopsis

```text
cis brd status [--workspace <path>] [--format <human|json|agent>]
```

## Effective statuses

| Status | Meaning |
| --- | --- |
| `Missing` | No canonical BRD exists. |
| `Review Required` | Required content or source assessment is incomplete. |
| `Ready for Approval` | Content and evidence are valid but no current human approval exists. |
| `Active` | A human approved valid content against matching participant baselines. |
| `Stale` | An Active BRD no longer matches its approved content, evidence, sources, or participant baselines. |

Automation may reduce `Active` to effective `Stale`; only `brd approve` may restore
`Active`. Status is read-only.

## Exit codes

| Code | Meaning |
| ---: | --- |
| `0` | Status was calculated. |
| `2` | Workspace or evidence configuration is invalid. |
| `4` | Canonical BRD is missing. |

## Related commands

- [`cis brd validate`](cis_brd_validate.md)
- [`cis brd approve`](cis_brd_approve.md)

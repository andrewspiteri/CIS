---
title: "cis brd validate"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-brd-validate
---

# `cis brd validate`

Validates canonical BRD structure, source assessment, participant evidence, and
approval readiness without changing files.

## Synopsis

```text
cis brd validate [--workspace <path>] [--format <human|json|agent>]
```

Validation requires the managed identity and blocks, all required sections without
TODO/TBD placeholders, current hashes for every discovered candidate, one allowed
assessment and rationale per candidate, readable graphs, and fresh participant graphs.

Participant baseline differences are reported as drift warnings. They do not prevent
an explicit approval because approval captures the then-current participant builds.
After approval, the same difference makes the effective state `Stale`.

## Exit codes

| Code | Meaning |
| ---: | --- |
| `0` | BRD content and evidence are valid. |
| `2` | Workspace or canonical configuration is invalid. |
| `4` | Canonical BRD or required graph evidence is missing. |
| `5` | BRD validation found blocking gaps. |

## Related commands

- [`cis brd status`](cis_brd_status.md)
- [`cis brd approve`](cis_brd_approve.md)
- [`cis brd init`](cis_brd_init.md)

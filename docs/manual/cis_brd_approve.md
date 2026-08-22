---
title: "cis brd approve"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-brd-approve
---

# `cis brd approve`

Records explicit human approval of a valid canonical BRD and captures current
participant graph baselines.

## Synopsis

```text
cis brd approve --reviewer <human> --reason <rationale>
  [--workspace <path>] [--format <human|json|agent>]
```

`--reviewer` and `--reason` are required. Approval records reviewer, UTC timestamp,
rationale, review date, `Active` document status, active catalog status, current
participant graph builds, and a normalized digest of the approved canonical content.
The authority graph should be rebuilt afterward so queries observe the approved BRD.

This is a human-authority command. Agents may prepare validation evidence and present
readiness, but must not invoke it without explicit user authorization for the reviewer
and rationale.

## Exit codes

| Code | Meaning |
| ---: | --- |
| `0` | Approval was recorded. |
| `2` | Workspace, reviewer, or rationale is invalid. |
| `4` | Canonical BRD or graph evidence is missing. |
| `5` | Validation gaps block approval. |

## Example

```powershell
cis brd approve --workspace C:\work\product-docs --reviewer "Product Council" --reason "Scope and requirements accepted"
```

## Related commands

- [`cis brd validate`](cis_brd_validate.md)
- [`cis brd status`](cis_brd_status.md)
- [`cis graph build`](cis_graph_build.md)

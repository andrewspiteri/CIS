---
title: "cis plan capability select"
type: command-reference
status: Active
owner: Andrew Spiteri
last_reviewed: "2026-08-13"
cis:
  stable_id: change-impact-studio:manual:cis-plan-capability-select
---

# `cis plan capability select`

## Synopsis

```text
cis plan capability select <capability-key> --type <task-type-key>
  [--replaces <old-task-type> ...] --reviewer <human> --reason <rationale>
  [--repo <path>] [--format <human|json|agent>]
```

Records a human-approved repository selection in the managed canonical capability
table. The selected type must be registered for the named capability. Every
`--replaces` key must be an extension type explicitly declared replaceable by the
selected provider. Core types are never replaceable.

The command is idempotent for the same effective selection, reviewer, and rationale.
It does not migrate existing tasks; use `cis plan task migrate-type` explicitly.

Exit code `0` means selected/unchanged; `2` means invalid input or authority;
`5` means other registered conflicts remain unresolved.


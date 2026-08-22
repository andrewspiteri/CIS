---
title: "cis plan task migrate-type"
type: command-reference
status: Active
owner: Andrew Spiteri
last_reviewed: "2026-08-13"
cis:
  stable_id: change-impact-studio:manual:cis-plan-task-migrate-type
---

# `cis plan task migrate-type`

## Synopsis

```text
cis plan task migrate-type <change-id> <task-id> --to <task-type-key>
  --reviewer <human> --reason <rationale>
  [--repo <path>] [--format <human|json|agent>]
```

Migrates one extension task instance to the repository-selected compatible type.
Both the canonical selection and target provider must authorize replacement. Core
task types cannot be migration sources or targets.

The command preserves stable task ID/path, completion evidence, deferrals/residual
risk, external issue links, and prior migration history. It updates provider/type
contract fields, adds any required predecessor dependencies, records the new
acceptance and validation checklist, returns the task and plan to Draft, and appends
an audited migration event. A re-import preserves these human-managed sections.

Exit code `0` means migrated/unchanged; `2` means the task, selection, provider,
replacement authorization, or required dependencies are invalid.


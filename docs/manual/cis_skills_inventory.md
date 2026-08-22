---
title: "cis skills inventory"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-15"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-skills-inventory
---

# `cis skills inventory`

Lists portable skills beneath `.github/skills/` and reports structural or metadata errors.

## Synopsis

```text
cis skills inventory [--repo <path>] [--format <human|json|agent>]
```

| Option | Default | Effect |
| --- | --- | --- |
| `--repo <path>` | Current directory | Selects an initialized repository. |
| `--format <format>` | `human` | Selects human, JSON, or line-oriented agent output. |

Inventory uses the same parser as validation and returns each declared name, repository-relative path, description, and line count. Diagnostics are omitted from human and agent inventory output; use `cis skills validate` for details. JSON retains the full structured result.

Exit code `0` means inventory completed without errors. Exit code `2` means repository configuration, format, skill structure, or required metadata is invalid. Warnings do not fail inventory.

## Related commands

- [`cis skills validate`](cis_skills_validate.md)
- [`cis skills import`](cis_skills_import.md)
- [`cis skills audit`](cis_skills_audit.md)
- [`cis repo init`](cis_repo_init.md)
- [`cis repo doctor`](cis_repo_doctor.md)

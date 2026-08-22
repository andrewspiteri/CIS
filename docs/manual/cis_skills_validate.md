---
title: "cis skills validate"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-15"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-skills-validate
---

# `cis skills validate`

Validates portable repository skill names, metadata, bodies, duplicates, and local resource links.

## Synopsis

```text
cis skills validate [--repo <path>] [--strict] [--fix] [--details] [--format <human|json|agent>]
```

| Option | Default | Effect |
| --- | --- | --- |
| `--repo <path>` | Current directory | Selects an initialized repository. |
| `--strict` | `false` | Treats warnings, including optional metadata or an oversized skill, as failures. |
| `--fix` | `false` | Applies bounded deterministic repairs for absent YAML front matter, missing `name` or `description`, an empty body, and a missing level-one heading before validation. |
| `--details` | `false` | Includes every valid skill record in agent output; the default agent response contains the summary, repairs, and diagnostics only. |
| `--format <format>` | `human` | Selects human, JSON, or line-oriented agent output. |

Validation is read-only unless `--fix` is supplied. Relative Markdown links may refer to files or directories elsewhere in the repository but may not escape it. It does not execute scripts referenced by skills.

`--fix` derives the skill name from its directory and creates a conservative description and title from the existing level-one heading or directory name. Existing body text and optional YAML fields are retained. A valid file that needs no repair is not rewritten. Malformed YAML, mismatched or invalid names, unsupported metadata, duplicate names, oversized files, and broken links are reported but not changed.

Statuses are `valid`, `valid-with-warnings`, and `invalid`. Exit code `0` means the selected policy passed. Exit code `2` means an error was found, strict mode found a warning, repository configuration is invalid, or the output format is unsupported.

Run strict validation after initialization or classification reconciliation:

```powershell
cis repo init --root docs/cis --yes
cis skills validate --fix --strict --format agent
```

## Related commands

- [`cis skills inventory`](cis_skills_inventory.md)
- [`cis skills import`](cis_skills_import.md)
- [`cis skills audit`](cis_skills_audit.md)
- [`cis repo doctor`](cis_repo_doctor.md)

---
title: "cis skills import"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-15"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-skills-import
---

# `cis skills import`

Discovers, stages, validates, and imports complete skill bundles from local paths, ZIP archives, or GitHub repositories.

## Synopsis

```text
cis skills import --source <path-or-url> [--source <path-or-url>...]
  [--repo <path>] [--fix] [--strict] [--dry-run] [--yes]
  [--format <human|json|agent>]
```

| Option | Required | Default | Effect |
| --- | --- | --- | --- |
| `--source <value>` | Yes | — | Local directory, local `SKILL.md`, local ZIP, GitHub repository or tree URL, or direct HTTP(S) ZIP URL. Repeat or provide several values. |
| `--repo <path>` | No | Current directory | Selects the initialized target repository. |
| `--fix` | No | `false` | Applies safe missing-YAML and heading repairs to staged copies. Original sources are never changed. |
| `--strict` | No | `false` | Rejects candidates that produce validation warnings. Errors always reject import. |
| `--dry-run` | No | `false` | Downloads and validates sources and reports the complete plan without changing the target repository. |
| `--yes` | No | `false` | Confirms creation of all planned bundles. |
| `--format <format>` | No | `human` | Selects human, JSON, or line-oriented agent output. |

## Supported URL forms

```text
https://github.com/<owner>/<repository>
https://github.com/<owner>/<repository>/tree/<ref>/<path>
https://github.com/<owner>/<repository>/archive/<ref>.zip
https://example.test/path/skills.zip
```

A GitHub repository URL downloads its default branch through `HEAD`. A tree URL downloads the named single-segment ref and restricts discovery to the requested path. Branch names containing `/` should use a direct archive URL because the GitHub tree URL is ambiguous without API access.

Generic webpages and individual remote Markdown files are rejected. A remote source must provide an archive so referenced scripts, assets, and guidance can be imported with `SKILL.md`.

## Import process and safety

1. Resolve the initialized target repository.
2. Download remote archives into an isolated temporary staging directory.
3. Enforce compressed, expanded-size, entry-count, and bundle-file limits.
4. Reject archive path traversal, symbolic links, and reparse points.
5. Discover skill bundles while excluding `.git`, `.cis`, `node_modules`, `bin`, and `obj` trees.
6. Copy candidates into a temporary initialized repository and run the normal skill validator.
7. Apply `--fix` only to staged copies when requested.
8. Compare deterministic bundle hashes with `.github/skills/<name>/`.
9. Require explicit confirmation before atomically creating new bundles.
10. Rebuild `.cis/local/skills/index.json` with current bundle paths, hashes, and source routing data.

Skill scripts are copied but never executed. Network redirects remain subject to the configured HTTP client. Downloads are limited to 100 MiB, expanded archives to 250 MiB and 10,000 entries, and each skill bundle to 2,000 files.

## Idempotency and conflicts

- Same name and bundle hash: `unchanged`.
- Same name and different bundle hash: conflict, exit `4`, and no overwrite.
- Duplicate skill names across supplied sources: error.
- Invalid candidate: error and no target changes.
- A dry run never writes the target repository or derived index.

All candidates are validated before target writes, and each new bundle is moved into place atomically. Existing skills are never replaced.

## Exit codes

| Code | Meaning |
| --- | --- |
| `0` | Dry run, successful import, or unchanged import. |
| `2` | Invalid repository, source, URL, archive, bundle, validation result, or output format. |
| `3` | Valid creates require explicit `--yes` confirmation. |
| `4` | A target skill name exists with different content. |

## Examples

Preview a GitHub repository import:

```powershell
cis skills import --source https://github.com/example/engineering-skills --dry-run
```

Import a specific folder and safely complete missing portable metadata:

```powershell
cis skills import `
  --source https://github.com/example/engineering-skills/tree/main/dotnet `
  --fix --strict --yes
```

Import several local collections:

```powershell
cis skills import --source ../shared-skills --source ./legacy/skills --yes
```

## Related commands

- [`cis skills inventory`](cis_skills_inventory.md)
- [`cis skills validate`](cis_skills_validate.md)
- [`cis skills audit`](cis_skills_audit.md)
- [`cis repo doctor`](cis_repo_doctor.md)

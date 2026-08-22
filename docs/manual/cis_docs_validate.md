---
title: "cis docs validate"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-08"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-docs-validate
---

# `cis docs validate`

Validates a repository's documentation catalog against the Markdown files beneath its configured documentation root.

## Synopsis

```text
cis docs validate [--repo <path>] [--format <human|json|agent>] [--strict]
```

## Options

| Option | Required | Default | Effect |
| --- | --- | --- | --- |
| `--repo <path>` | No | Current directory | Selects the initialized target repository. |
| `--format <format>` | No | `human` | Selects `human`, `json`, or `agent` output. Values are case-insensitive. |
| `--strict` | No | `false` | Treats warnings as validation failures in addition to errors. |
| `-?`, `-h`, `--help` | No | — | Shows command help and exits without validating documentation. |

## Operation and effects

The command reads `.cis/repository.yml`, resolves the configured documentation root, reads `<root>/catalog.yml`, inventories Markdown files, and checks:

- catalog schema version `1`;
- required `id`, `path`, `type`, `status`, and `authority` values;
- case-insensitive duplicate IDs and paths;
- stable-ID character rules;
- authority values: `canonical`, `derived`, `proposal`, or `routing`;
- repository-relative paths that remain inside the documentation root and target existing `.md` files;
- valid YAML front matter in cataloged documents;
- cataloged documents without front matter; and
- Markdown files missing from the catalog.

Malformed front matter in a cataloged document is an error. Missing front matter and uncataloged Markdown are warnings. Without `--strict`, warnings are reported but do not fail validation; with `--strict`, any warning produces exit code `5`.

The command is read-only and does not repair the catalog or documents.

## Output

Human output reports status, catalog-entry count, Markdown-document count, warnings, and errors. JSON emits the complete result using camel-case property names. Agent output emits a summary such as:

```text
status=<status>;exitCode=<code>;valid=<true|false>;catalogEntries=<count>;markdownDocuments=<count>;warnings=<count>;errors=<count>;strict=<true|false>
```

The repository and documentation root follow on separate lines, followed by any `warning=` and `error=` records.

## Exit codes

| Code | Meaning |
| --- | --- |
| `0` | Validation passed. In non-strict mode, this can include warnings. |
| `2` | Repository context is invalid or the output format is unsupported. |
| `5` | Validation errors exist, or `--strict` was used and warnings exist. |

## Examples

Validate the current repository:

```powershell
cis docs validate
```

Use strict validation in an automated workflow:

```powershell
cis docs validate --strict --format agent
```

Validate another initialized repository and return JSON:

```powershell
cis docs validate --repo C:\work\orders --strict --format json
```

## Related commands

- [`cis repo init`](cis_repo_init.md)
- [`cis docs inventory`](cis_docs_inventory.md)

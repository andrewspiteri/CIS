---
title: "cis repo doctor"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-repo-doctor
---

# `cis repo doctor`

Inspects a repository for CIS readiness problems and reports evidence-backed suggested fixes. The command is read-only: it does not apply fixes or change repository files. An optional root hint lets it diagnose initialization failures that occurred before configuration was established.

## Synopsis

```text
cis repo doctor [--repo <path>] [--root <repository-relative-path>] [--format <human|json|agent>]
```

## Options

| Option | Required | Default | Effect |
| --- | --- | --- | --- |
| `--repo <path>` | No | Current directory | Selects the target repository. |
| `--root <path>` | No | Configured root | Supplies the documentation root attempted by a failed init when no valid repository configuration exists yet. |
| `--format <format>` | No | `human` | Selects `human`, `json`, or `agent` output. Values are case-insensitive. |
| `-?`, `-h`, `--help` | No | — | Shows command help without running checks. |

There is no LLM-enablement option. Doctor always probes Ollama and warns when it is unavailable.

After a failed initialization, reuse the same repository and root:

```text
cis repo doctor --repo <repository> --root <documentation-root> --format agent
```

## Checks

The repository module checks:

- `.cis/repository.yml` and the configured documentation root;
- unapplied `cis repo init` creates or managed updates;
- managed-content collisions;
- classification warnings;
- a root ESLint 9+ lint script without the required flat configuration; and
- Ollama availability and locally installed model names.

Loaded modules may contribute further checks through `ICisRepositoryDoctorCheck`. The documentation module contributes:

- catalog, stable-ID, path, front-matter, and registration validation;
- Draft document reporting; and
- unresolved `TODO` marker reporting.

The graph module contributes:

- missing local graph generation reporting;
- structural, identity, endpoint, evidence, and sensitive-property diagnostics;
- stale manifest/input reporting; and
- accidental Git tracking of `.cis/local/` artifacts.

The index module contributes file-card availability and source-hash freshness checks.
It suggests bounded `cis index build --limit 100` batches when coverage is missing or
stale. Doctor never invokes a model; it only inspects derived card metadata and source
hashes.

The skills module contributes portable skill structure, name, metadata, duplicate,
body, size, and local-resource-link diagnostics. Suggested fixes route through
`cis skills validate --strict`. Missing YAML fields or headings route through the
explicit `cis skills validate --fix --strict` repair; doctor itself never rewrites skill content.

Ollama is read from `OLLAMA_HOST`, or `http://127.0.0.1:11434` when the variable is not set. Doctor calls the local `/api/tags` endpoint with a two-second timeout. This availability probe does not submit repository content or run a model.

## Findings

Every finding contains:

| Field | Meaning |
| --- | --- |
| `code` | Stable diagnostic identity such as `CIS-REPO-005`. |
| `severity` | `error`, `warning`, or `info`. |
| `category` | Diagnostic area such as `reconciliation`, `documentation`, or `local-llm`. |
| `message` | Concise observed condition. |
| `evidence` | Repository paths, model names, or diagnostic details supporting the finding. |
| `suggestedFix` | Recommended maintainer action. |
| `fixCommand` | Optional command to preview or perform the next step. It is never executed automatically. |
| `fixability` | `none`, `manual`, or `review-required`. |

An unavailable Ollama service produces warning `CIS-OLLAMA-001`. A running service with no models produces `CIS-OLLAMA-002`. A ready service produces informational finding `CIS-OLLAMA-003` with its model names.

## Output

Human output prints the status, repository, documentation root, Ollama state, severity totals, evidence, and suggested fix for each finding.

JSON emits the complete result with camel-case property names. Agent output begins with summary records and emits one `finding=` record followed by `evidence.<code>=`, `suggestedFix.<code>=`, and optional `fixCommand.<code>=` records.

Common statuses are `healthy`, `warnings`, and `errors`. Warnings do not cause a failing exit code.

## Exit codes

| Code | Meaning |
| --- | --- |
| `0` | Checks completed with no errors. Warnings and informational findings may be present. |
| `2` | Repository configuration is invalid, the repository is not initialized, or the output format is unsupported. |
| `5` | At least one error-level readiness finding was reported. |

## Examples

Inspect the current repository:

```powershell
cis repo doctor
```

Inspect another repository using agent-oriented output:

```powershell
cis repo doctor --repo C:\work\orders --format agent
```

Diagnose an init failure before `.cis/repository.yml` exists:

```powershell
cis repo doctor --repo C:\work\orders --root docs/cis --format agent
```

Use JSON for a Visual Studio Code client:

```powershell
cis repo doctor --format json
```

## Related commands

- [`cis repo init`](cis_repo_init.md)
- [`cis docs inventory`](cis_docs_inventory.md)
- [`cis docs validate`](cis_docs_validate.md)
- [`cis graph validate`](cis_graph_validate.md)
When the API module is loaded, doctor also reports a missing repository API profile,
missing `.cis/local/api/` discovery state for API-bearing repositories, and persisted
API governance diagnostics. Suggested fixes route through `cis api discover` and
`cis api validate --strict`; doctor itself remains read-only.

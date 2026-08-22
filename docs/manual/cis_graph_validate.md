---
title: "cis graph validate"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-20"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-graph-validate
---

# `cis graph validate`

Validates an existing derived graph generation without rebuilding or changing it.

## Synopsis

```text
cis graph validate [--strict] [--repo <path> | --workspace <path>]
  [--format <human|json|agent>]
```

## Options

| Option | Required | Default | Effect |
| --- | --- | --- | --- |
| `--strict` | No | `false` | Treats warning diagnostics as validation failures. |
| `--repo <path>` | No | Current directory | Selects an initialized repository. |
| `--workspace <path>` | No | — | Validates every repository graph registered in `.cis/workspace.yml`. |
| `--format <format>` | No | `human` | Selects `human`, `json`, or `agent`. |
| `-?`, `-h`, `--help` | No | — | Shows command help without reading the graph. |

## Validation scope

The command checks:

- SQLite storage schema compatibility and readable normalized graph state;
- graph and manifest schema, repository identity, build identity, and extractor set;
- duplicate or malformed node identities and unsupported kinds or authorities;
- lifecycle and normalized-facet registration;
- repository-contained, existing node locations and provenance paths;
- evidence method, extractor, confidence, and content-hash shape;
- governed reference identities and source-file path identities;
- missing component identity on symbols and tests;
- duplicate edges, registered edge types/states/confidence, endpoint existence, and
  allowed source/target kinds;
- durable provenance for proposed and confirmed relationships;
- duplicate, missing, changed, unreadable, and newly discovered manifest inputs;
- potential sensitive values copied into node properties;
- warning/error diagnostics retained from `cis graph build`; and
- derived `.cis/local/` artifacts accidentally tracked by Git.

Validation reconstructs the portable graph contract from SQLite only when whole-graph
structural inspection is required. Normal graph queries continue to read bounded
indexed rows.

Input freshness uses the same managed-input normalization as `cis graph build`. Catalog
and dossier routes owned by CIS therefore do not invalidate the generation that wrote
them, while human-authored and product-source changes remain visible. Governed lifecycle
values include planned, active, adopted, archived, deprecated, retired, implemented, and completed
states used by canonical documentation and delivery evidence.

Validation is deterministic and does not execute target assemblies or call a model.
It reports contradictions that are structurally represented in the graph; deeper
semantic contradiction analysis remains outside this validation slice.

## Freshness and strict mode

Changed, missing, or new graph inputs produce warnings and set freshness to `stale`.
The graph remains available for inspection. Run `cis graph build` to create a fresh
generation.

Without `--strict`, a structurally healthy graph with warnings exits successfully.
With `--strict`, any warning produces exit code `5`. Errors always produce code `5`.
Workspace validation applies the selected strictness independently to every graph and
reports all repository results; the highest-severity repository exit code becomes the
aggregate exit code.

## Statuses

| Status | Meaning |
| --- | --- |
| `valid` | No validation diagnostics were found. |
| `warnings` | The graph is structurally usable but has warnings. |
| `invalid` | One or more structural errors were found. |
| `graph-unavailable` | The stored generation is missing, unreadable, or incompatible. |
| `invalid-repository` | CIS repository configuration could not be resolved. |

## Exit codes

| Code | Meaning |
| --- | --- |
| `0` | Validation passed, including warnings when strict mode is disabled. |
| `2` | Repository context or output format is invalid. |
| `4` | The graph generation is unavailable or incompatible. |
| `5` | Structural errors exist, or strict mode rejects warnings. |

## Examples

```powershell
cis graph validate
cis graph validate --strict --format agent
cis graph validate --repo C:\work\orders --format json
cis graph validate --workspace C:\work\commerce --format agent
```

## Related commands

- [`cis graph build`](cis_graph_build.md)
- [`cis graph find`](cis_graph_find.md)
- [`cis repo doctor`](cis_repo_doctor.md)
- [`cis repo import`](cis_repo_import.md)

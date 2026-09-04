---
title: "cis graph build"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-16"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-graph-build
---

# `cis graph build`

Builds a deterministic, disposable local context graph from the configured repository and its canonical documentation. The command never changes Markdown or the documentation catalog.

## Synopsis

```text
cis graph build [--repo <path> | --workspace <path>] [--refresh]
  [--format <human|json|agent>]
```

## Options

| Option | Required | Default | Effect |
| --- | --- | --- | --- |
| `--repo <path>` | No | Current directory | Selects an initialized target repository. |
| `--workspace <path>` | No | — | Builds every repository registered in `.cis/workspace.yml` instead of one repository. |
| `--refresh` | No | `false` | Forces full extraction even when the content-addressed graph status cache is current. |
| `--format <format>` | No | `human` | Selects `human`, `json`, or `agent` output. |
| `-?`, `-h`, `--help` | No | — | Shows command help without building the graph. |

After documentation inventory and input hashing, an unchanged build reads node/edge counts
and diagnostics from the SQLite header and returns `unchanged` without repeating compiler
analysis or graph extraction. Extractor versions and exact input hashes remain part of the
build identity. Use `--refresh` for an intentional deep regeneration; it rewrites only
disposable `.cis/local/graph/` state.

## Current extraction scope

The graph builder creates:

- one repository node from `.cis/repository.yml`;
- document nodes from every documentation-catalog entry;
- component nodes from the canonical repository profile;
- reference-item nodes from supported governed reference tables;
- source-file nodes for supported implementation and test files;
- compiler-bound C# declarations, attributes, interfaces and base types, repository and external
  calls, generic arguments, bounded invocation argument shapes, generated-code markers,
  call ordering and intraprocedural semantic flow summaries; lexical declarations for
  other supported languages; and framework-recognized test cases;
- canonical change-local decision rows and catalogued architecture decisions, with
  confirmed promotion provenance;
- external package and unresolved project dependency nodes from supported manifests;
- GitHub Actions workflow-job nodes; and
- `contains`, `describes`, `declares`, `governed-by`, `owns`, `belongs-to`,
  `depends-on`, `calls`, `annotated-by`, `implements`, `inherits`, `invokes`, `targets`, `precedes`,
  `has-dataflow`, semantic boundary edges, `implemented-by`, and evidence-qualified
  `verified-by` edges; and
- evidence, confidence, authority, lifecycle, and source locators for every generated fact.

Supported reference families are API, command, event, workflow state, business invariant, projection, permission, configuration, package, screen/route, problem details, module ownership, data dictionary, ERD, and traceability.

Source scanning supports C#, TypeScript/JavaScript, Swift, Kotlin, Python, SQL, and
Terraform files. Symbol adapters currently recognize addressable declarations in C#,
TypeScript/JavaScript, Swift, Kotlin, and Python. Test adapters recognize xUnit/NUnit/
MSTest-style C# attributes, JavaScript `it`/`test`, XCTest naming, Kotlin `@Test`,
and pytest naming.

C# sources additionally use Roslyn semantic binding. The compiler adapter creates
stable type, method, constructor, property, accessor, and local-function symbols with
qualified signatures. It records attributes, effective interfaces, base types, generated-source
markers, and high-confidence calls to repository-owned and metadata symbols. Every
resolved call receives a `calls` edge carrying generic types, argument shapes, semantic
categories, and source ordering. A distinct invocation node is retained only for calls
whose generic or semantic facts need an addressable site; ordering nodes are restricted
to recognized flow milestones. Argument values are deliberately not stored. Bounded dataflow records recognized call milestones within one callable symbol;
it does not claim interprocedural, branch-sensitive, or runtime flow. Test methods also
produce test-to-symbol `calls` edges. Lexical extraction remains the deterministic
fallback for unbound syntax and other supported languages.

Dependency extraction supports `.csproj`, `package.json`, Gradle build files, and
`Package.swift`. A project reference creates `depends-on`; it never proves behavioral
verification. `verified-by` is emitted only when a recognized test contains the exact
stable identity of a governed reference item. A reference item's explicit `Evidence`
path may create `implemented-by` to an exact source file.

## Build pipeline

The command:

1. resolves `.cis/repository.yml` and the documentation root;
2. validates the documentation catalog and cataloged Markdown;
3. hashes repository configuration, cataloged Markdown, supported source/test files,
   project manifests, and GitHub Actions workflows;
4. derives a build ID from graph schema, extractor versions, and input hashes;
5. extracts and validates typed nodes and edges;
6. migrates a supported older SQLite storage schema when required;
7. content-hash compares nodes and edges with the existing generation;
8. transactionally upserts changed rows and removes obsolete rows; and
9. writes human-inspectable manifest and diagnostics mirrors after the database commit.

Graph construction is deterministic and does not require Ollama or another model.

With `--workspace`, CIS resolves and validates the workspace registry before building
each repository's independent graph. It reports per-repository status, build identity,
diagnostics, node and edge counts, plus aggregate counts. A repository failure is
visible in the aggregate result and determines the command exit code. The operation
does not merge graphs or invent cross-repository edges; federation remains a bounded
query-time operation.

## Derived outputs

```text
.cis/local/graph/
  context.db
  manifest.json
  diagnostics.json
```

- `context.db` is the versioned normalized SQLite query store.
- `manifest.json` contains the build ID, Git baseline when available, input hashes, and extractor versions.
- `diagnostics.json` contains complete extraction and validation diagnostics.

The database contains normalized build, repository, node, facet, location, edge,
evidence, diagnostic, input, extractor, and FTS5 search tables. Indexed queries do
not load or parse a repository-wide JSON document. Portable JSON remains available
through `cis graph export --type json`.

Source and target indexes use fixed-size hashes while retaining and verifying full
graph keys in relationship rows. The FTS5 projection is contentless and uses trigram
tokenization for indexed substring routing. SQLite
incremental auto-vacuum reclaims free pages after graph changes; a storage migration
performs a full compaction when its physical layout changes.

The directory is ignored by Git and may be deleted at any time. Rebuilding an
unchanged repository reports `unchanged` and does not rewrite these files. A changed
build updates only changed node and edge rows. Supported older database schemas are
migrated using SQLite `user_version`; incompatible disposable state is safely rebuilt.

## Diagnostics and safety

Missing reference tables, missing identity columns, incompatible duplicate node
identities, broken edge endpoints, unreadable inputs, and documentation validation
errors prevent replacement of the last healthy graph.

Graph persistence runs in one SQLite transaction with foreign-key validation before
commit. Failure rolls back the generation. When an incompatible database must be
replaced, CIS builds and validates a temporary database before moving it into place.

Template placeholder or TODO reference identities are skipped as informational
diagnostics because they are not asserted facts. Malformed non-placeholder identities
remain warnings. A graph built with warnings has `partial` build status. Query consumers
must expose that status rather than imply completeness.

Ordinary calls do not receive redundant invocation nodes, which keeps derived storage
proportional to useful semantic facts. SQLite indexes trade some disk space for bounded
memory and indexed identity, adjacency, evidence-path, and text queries.

The command does not:

- modify canonical documentation;
- execute target-repository assemblies;
- accept proposed relationships;
- infer verification from naming or project references;
- call an LLM; or
- treat deterministic discovery as human confirmation.

## Output

Human output reports status, build ID, node and edge totals, output path, and diagnostics. JSON emits the complete `GraphBuildResult`. Agent output emits stable summary fields followed by `diagnostic=` and `evidence.<code>=` records.

Statuses are:

| Status | Meaning |
| --- | --- |
| `built` | A complete graph generation was written. |
| `partial` | A graph was written with warning-level gaps. |
| `unchanged` | Inputs and extractor versions match the existing build. |
| `failed` | Graph errors prevented replacement of the last healthy generation. |
| `invalid-repository` | CIS repository configuration could not be resolved. |

## Exit codes

| Code | Meaning |
| --- | --- |
| `0` | A complete or partial graph was built, or the graph was already unchanged. |
| `2` | Repository context or output format is invalid. |
| `5` | Graph or canonical-input validation failed. |

## Examples

Build the current repository graph:

```powershell
cis graph build
```

Build another repository and emit agent output:

```powershell
cis graph build --repo C:\work\orders --format agent
```

Build every imported repository graph:

```powershell
cis graph build --workspace C:\work\commerce --format agent
```

## Related commands

- [`cis graph validate`](cis_graph_validate.md)
- [`cis repo init`](cis_repo_init.md)
- [`cis repo import`](cis_repo_import.md)
- [`cis repo doctor`](cis_repo_doctor.md)
- [`cis docs validate`](cis_docs_validate.md)

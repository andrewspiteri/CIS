---
title: "Change Impact Studio Context Model and Local Graph"
type: specification
status: Draft
version: "0.1"
scope: "Product:ChangeImpactStudio"
owner: "Andrew Spiteri"
last_reviewed: "2026-08-09"
review_cadence: "on change"
cis:
  stable_id: change-impact-studio:spec:context-model-and-graph
---

# Context Model and Local Graph

## 1. Purpose

This specification defines how CIS turns repository knowledge into a typed,
queryable local graph. It covers catalog documents, specifications, references,
components, contracts, source evidence, workflows, and tests.

The graph supports repository understanding, impact analysis, planning, bounded
agent context, and verification. It is a routing and reasoning aid, not proof that
all impacts have been found.

## 2. Source-of-truth boundary

The graph is always derived. Canonical and authoritative repository inputs remain:

- `.cis/repository.yml` for repository identity and the documentation root;
- `<documentation-root>/catalog.yml` for document identity and routing metadata;
- cataloged Markdown, including specifications, ADRs, references, and change records;
- stable rows and identifiers in governed Markdown reference tables;
- source, tests, project metadata, workflows, and configuration as implementation evidence; and
- reviewed relationship declarations or proposal dispositions in repository-owned records.

The generated graph must never become the only location of a durable fact. Editing
or deleting it cannot change canonical meaning. A graph node marked `canonical`
projects a canonical source; the graph record itself is still derived.

The product/source graph build identity excludes managed `changes/CIS-NNNN/` input
hashes and normalizes their catalog entries out of the identity calculation. Those
documents may still be extracted as graph content, but creating evidence, recording
approval, or updating a baseline field cannot self-invalidate the product baseline.
All non-dossier catalog, specification, reference, source, test, configuration, and
workflow inputs remain fingerprinted and freshness-checked.

## 3. Module boundary

The `graph` module owns construction, validation, traversal, and export:

```text
cis graph build
cis graph validate
cis graph find
cis graph related
cis graph trace
cis graph export
```

The `context` module consumes the graph and canonical sources to build bounded packs:

```text
cis context search
cis context pack
cis context contract
cis context symbol
cis context references
cis context callers
cis context tests-for
```

Context must not define competing graph semantics. Shared node, edge, provenance,
and query contracts belong in `Cis.Abstractions` when implementation begins.

## 4. Graph model

The graph is a directed property graph with typed nodes, typed edges, and evidence
attached to every derived fact.

### 4.1 Node contract

| Field | Meaning |
|---|---|
| `key` | Derived repository-unique lookup key |
| `repositoryId` | Stable repository identity from `.cis/repository.yml` |
| `kind` | Registered base node kind |
| `subtype` | Repository-extensible specific classification |
| `localId` | Stable identity within repository and kind |
| `label` | Human-readable display name |
| `facets` | Capabilities such as `contract`, `canonical-document`, or `test` |
| `authority` | `canonical`, `routing`, `derived`, or `proposal` |
| `lifecycle` | Source lifecycle such as `draft`, `active`, `deprecated`, or `withdrawn` |
| `locations` | One or more repository source locators |
| `properties` | Type-specific scalar or bounded-list data |
| `provenance` | How the node was declared, discovered, proposed, or confirmed |

Large document bodies, source files, embeddings, and model prompts are not stored in
node properties. Queries return locators so callers retrieve canonical content only
when needed.

### 4.2 Initial node kinds

| Kind | Purpose | Typical identity source |
|---|---|---|
| `repository` | Current repository workspace | `repository.id` |
| `component` | Application, library, worker, client, infrastructure, test, or tooling boundary | Confirmed repository-profile component ID |
| `document` | Cataloged Markdown document | Catalog/front-matter stable ID |
| `document-section` | Addressable normative section | Document ID plus explicit section/requirement ID |
| `requirement` | Product, technical, security, operational, or quality requirement | Explicit requirement ID |
| `decision` | ADR or change-local durable decision | ADR/decision stable ID |
| `reference-item` | Stable row in a governed reference | Family plus row stable ID |
| `source-file` | Repository implementation or test file | Normalized repository-relative path |
| `symbol` | Addressable type, member, handler, or declaration | Component plus language-qualified symbol identity |
| `invocation` | A compiler-bound call site whose generic/semantic argument shape or flow ordering must remain independently addressable | Caller symbol plus repository path and syntax span |
| `dataflow` | Bounded, intraprocedural semantic milestone ordering for one callable symbol | Caller symbol plus compiler adapter |
| `test` | Test project, suite, case, or contract test | Component plus framework-qualified test identity |
| `workflow` | CI, build, deployment, migration, or operational workflow | File plus stable job/target identity |
| `dependency` | External package, service, platform, or repository dependency | Ecosystem plus normalized identity |

Specifications and references are `document` nodes whose subtype comes from the
catalog. Stable entries inside a reference become `reference-item` nodes. CIS does
not create duplicate specification, reference, or contract nodes for the same fact.

### 4.3 Reference and contract facets

| Reference family | Node subtype | Identity fields | Typical facets |
|---|---|---|---|
| API dictionary | `api-operation` | API ID | `contract` |
| Command dictionary | `command` | Command ID | `contract`, `domain-behaviour` |
| Event dictionary | `event` | Event ID | `contract`, `domain-behaviour` |
| Workflow-state dictionary | `workflow-state` | Workflow ID + State | `domain-behaviour` |
| Business-invariant catalogue | `business-invariant` | Invariant ID | `domain-behaviour`, `rule` |
| Projection dictionary | `projection` | Projection ID | `domain-behaviour`, `data` |
| Permissions dictionary | `permission` | Permission code | `contract`, `security` |
| Configuration dictionary | `configuration-key` | Owner + Path | `contract`, `configuration` |
| Package catalogue | `package` | Component + Package | `dependency` |
| Screen and route map | `screen-route` | Component + Platform + Screen/route | `navigation`, `contract` |
| Problem-details catalogue | `problem` | Problem ID | `contract` |
| Module-ownership map | `module-boundary` | Module ID | `ownership` |
| Data dictionary | `entity-field` | Entity + Field | `contract`, `data` |
| ERD | `entity-relationship` | Entity + Relationship + Target | `data` |
| Traceability matrix | `trace-link` | Trace ID | `traceability` |

The paired specification governs the table and designates its identity fields.
Single-ID families use that value directly. Composite families encode normalized
field values in the listed order; the raw fields remain properties. A missing or
duplicate identity produces a diagnostic and cannot become a durable graph target.

## 5. Stable identity

Identity is the structured tuple:

```text
(repositoryId, kind, localId)
```

The derived lookup key is `<repositoryId>::<kind>::<localId>`. `::` is reserved.
Consumers treat `key` as opaque and use structured fields for display and export.
Reference-item local IDs use `<subtype>/<encoded identity parts>` so IDs from two
reference families cannot collide.

Identity rules are:

- content edits do not change identity;
- paths are locations, not document, component, contract, requirement, or symbol identities;
- catalog/front-matter stable IDs take precedence over inferred document IDs;
- reference rows use governed stable IDs;
- symbols use component ID plus language-qualified name and overload signature;
- tests use component ID plus framework-qualified suite/case identity;
- source-file nodes deliberately use repository-relative path identity; and
- inferred identities are `derived` and cannot be canonical relationship targets
  until promoted to a stable source ID.

Implementation discovery prunes dependency, legacy, and generated-output trees before file
enumeration. In particular, `node_modules`, `_old`, `build_out`, and `nongit` are excluded at any repository depth and
must not produce source-file, symbol, dependency, workflow, or test nodes. Package
manifests owned by the repository remain graph inputs; installed package copies do not.

Aliases may preserve discovery after rename, but only one key is current. Alias
conflicts are validation errors.

## 6. Edge contract

| Field | Meaning |
|---|---|
| `key` | Derived key for source/type/target |
| `type` | Registered directed relationship type |
| `from` | Source node key |
| `to` | Target node key |
| `state` | Effective relationship state |
| `confidence` | `high`, `medium`, or `low`, with a reason |
| `observations` | Declarations, discoveries, proposals, and dispositions supporting the edge |
| `properties` | Type-specific bounded metadata |

### 6.1 Initial edge types

| Edge type | Direction and meaning |
|---|---|
| `contains` | Repository contains component/document; document contains section/item |
| `declares` | Document declares requirement, decision, or reference item |
| `references` | Source explicitly references another node |
| `governed-by` | Node is governed by a specification, policy, standard, or ADR |
| `describes` | Document/section describes a component, contract, workflow, or symbol |
| `owns` | Component owns a contract, symbol, workflow, or data surface |
| `belongs-to` | File, symbol, test, or workflow belongs to a component |
| `implemented-by` | Requirement or contract is implemented by component or symbol |
| `consumed-by` | Contract, package, event, or configuration is consumed by component/symbol |
| `produced-by` | Contract, package, event, or artifact is produced by component/symbol |
| `depends-on` | Component, symbol, workflow, or dependency relies on another node |
| `calls` | Compiler-bound symbol or test invokes a repository-owned symbol |
| `annotated-by` | Compiler-bound symbol declares or inherits an attribute |
| `implements`, `inherits` | Compiler-bound type directly implements an interface or inherits a base type |
| `invokes` | Callable symbol owns a specific invocation site |
| `targets` | Invocation site resolves to a repository or external symbol |
| `precedes` | Invocation site occurs before another invocation in the same callable symbol |
| `delegates` | Callable symbol invokes a repository-owned symbol |
| `has-dataflow` | Callable symbol owns a bounded compiler-derived call-order summary |
| `binds-options`, `reads-configuration` | Callable symbol uses typed configuration binding or direct key access |
| `logging-call`, `structured-log`, `unstructured-log` | Callable symbol emits logging with classified argument shapes |
| `persistence-call`, `transaction-call`, `outbox-write`, `event-publish`, `idempotency-check` | Callable symbol reaches a recognized operational boundary |
| `publishes` | Component or symbol publishes an event contract |
| `subscribes-to` | Component or symbol subscribes to an event contract |
| `configured-by` | Component or workflow is configured by a configuration item |
| `deployed-by` | Component is deployed by a workflow or infrastructure node |
| `verified-by` | Requirement, contract, component, symbol, or workflow is verified by a test |
| `generated-from` | Artifact or symbol is generated from another contract or source |
| `supersedes` | Decision, document, requirement, or contract replaces an earlier node |

Queries may expose inverse labels such as `implements`, `consumes`, `produces`, and
`verifies`, but only the registered direction is persisted. Validation checks allowed
source and target kinds.

### 6.2 Canonical relationship declarations

Document-level relationships may be declared in Markdown front matter:

```yaml
cis:
  stable_id: orders:spec:checkout
  relationships:
    - type: governed-by
      target_kind: document
      target_id: orders:adr:payment-boundary
```

Relationships for requirements and reference rows use governed stable-ID columns or
relationship fields in their Markdown tables. A normal Markdown link creates a
`discovered` `references` edge; it becomes `declared` only when represented in an
explicit relationship field. Human confirmation/rejection belongs in an ADR, change
record, or reviewed proposal record rather than in local graph state.

### 6.3 Relationship state

| State | Meaning | Default traversal |
|---|---|---|
| `declared` | Explicit canonical relationship | Included |
| `discovered` | Deterministically observed | Included |
| `proposed` | Heuristic/model suggestion awaiting review | Opt-in |
| `confirmed` | Human-confirmed proposal or relationship | Included |
| `rejected` | Human-rejected proposal retained for audit | Excluded |
| `possibly-stale` | Relationship no longer supported | Opt-in with warning |

Deterministic does not mean canonical, and high confidence does not mean confirmed.
Proposal and review state must come from durable change/proposal records or a clearly
local proposal source. Rebuilding must not lose a human disposition.

Multiple observations for one source/type/target tuple are retained. The graph must
not silently discard a rejection, stale declaration, or conflicting observation.

## 7. Evidence and provenance

Every discovered node and edge has at least one evidence locator:

```yaml
source_kind: canonical-markdown
path: docs/references/api-dictionary.md
locator:
  kind: table-row
  value: orders-api:get:orders-id
content_hash: sha256:...
method: declared
extractor: cis.markdown.reference-table/1
confidence: high
observed_commit: abc123
```

Locators support document stable IDs, headings, requirement IDs, table row IDs,
files, qualified symbols, approximate display line ranges, JSON/YAML pointers,
project declarations, workflow targets, and test identities.

Line numbers and excerpts never establish identity. Hashes detect staleness.
Secrets, sensitive values, full source bodies, raw prompts, and large logs are not
copied into the graph.

Provenance records method, extractor/version, locator/hash, confidence/reason, Git
commit and dirty state, model/provider/run identity for proposals, and review record
for confirmed or rejected proposals.

### 7.1 Component and source evidence

Confirmed repository-profile rows produce canonical component nodes. Classifier-only
components remain derived/discovered until a maintainer confirms the profile.

Supported source files produce `source-file` nodes after standard exclusions such as
`.git`, `.cis/local`, build outputs, dependencies, and generated caches. Language
adapters may add symbol nodes. A symbol identity requires a component, qualified
name, kind, and overload signature when applicable; text occurrence alone cannot
establish symbol identity.

Project references, package declarations, route mappings, event registrations, and
other structurally recognized syntax may create discovered edges. Plain name
similarity is low-confidence proposal evidence, not a deterministic relationship.

### 7.2 Test evidence

Tests may be represented at project, suite, and case level. A test project reference
creates `depends-on`; it does not by itself prove `verified-by`. A `verified-by` edge
requires stronger evidence such as an explicit contract/requirement ID, a resolved
call or symbol reference in test code, framework metadata, or a reviewed declaration.

Naming similarity may propose a test relationship but is excluded from default
traversal. Query results state the evidence level so “tests-for” never implies that a
structural project dependency proves behavioral coverage.

## 8. Build pipeline

```text
resolve repository context
-> validate catalog and canonical inputs
-> create repository and document nodes
-> read confirmed component profile
-> parse governed specifications and references
-> scan project metadata, source, tests, and workflows
-> resolve identities and merge observations
-> validate nodes, edges, and evidence
-> write one atomic local generation
```

The deterministic build works without Ollama. Model or heuristic relationships are a
separate proposal layer and remain `proposed` until reviewed.

Metadata precedence is:

1. reviewed canonical declaration;
2. catalog/front-matter identity and authority;
3. governed Markdown content;
4. deterministic implementation evidence;
5. deterministic inference;
6. heuristic/model proposal.

Precedence resolves metadata, not contradictions. When documentation and
implementation disagree, both observations remain and a drift diagnostic is emitted.

Extraction may recompute the graph from recorded input hashes and extractor versions,
but SQLite persistence compares node and edge content hashes and updates only changed
rows. A successful build commits one database transaction. Fatal identity, schema,
path, or persistence errors leave the last healthy graph untouched. Warnings may
produce a graph marked `partial`. Every query reports build identity and freshness.

## 9. Derived local storage

```text
.cis/local/graph/
  context.db
  manifest.json
  diagnostics.json
  export.json                 # only when explicitly requested
```

| File | Purpose |
|---|---|
| `context.db` | Versioned SQLite query projection containing graph builds, repositories, nodes, edges, evidence, diagnostics, source inputs, and full-text routing data |
| `manifest.json` | Schema/build ID, repository baseline, dirty state, input hashes, extractor versions |
| `diagnostics.json` | Human-inspectable mirror of duplicate, broken, ambiguous, stale, and unsupported facts |
| `export.json` | Optional portable graph produced by `cis graph export --type json` |

`.cis/.gitignore` excludes `local/`. Graph commands report an error if local graph
artifacts are tracked. `buildId` derives from schema version, normalized input hashes,
and extractor versions. Nodes/edges use stable key ordering; timestamps do not affect
semantic comparison.

`cis graph status` reads only the SQLite build header, input manifest, and stored
diagnostics before checking current input hashes. Repository Doctor uses this bounded
path and does not deserialize or traverse every graph row. `cis graph validate` remains
the explicit deep-integrity command. An unchanged `cis graph build` returns from the
content-addressed header before compiler and graph extraction; `--refresh` forces a full
disposable regeneration when toolchain or diagnostic investigation requires it.

SQLite is the operational derived store. It is never canonical and deleting
`.cis/local/` must remain safe. Markdown, repository configuration, and governed
reference files remain authoritative. Portable JSON is an explicit export rather
than a build side effect.

The database uses `PRAGMA user_version` for storage migrations independently of the
portable graph schema. A build migrates supported older storage schemas and replaces
an incompatible disposable database only after a healthy replacement has been
created. Node and edge content hashes allow changed generations to update only
changed rows; removed identities are deleted in the same transaction. A failed
transaction leaves the last healthy generation queryable.

The normalized schema includes `repositories`, `graph_builds`, `source_inputs`,
`extractors`, `nodes`, `node_facets`, `node_locations`, `edges`, `evidence`,
`node_evidence`, `edge_evidence`, `diagnostics`, and the FTS5 `node_search` index.
Indexes cover structured identity, kind/subtype, facets, source and target adjacency,
relationship type/state, and evidence paths. `find`, `related`, and `trace` query
these tables directly and do not deserialize a repository-wide graph document.
Adjacency indexes use fixed-size SHA-256 key projections and verify the original key
in the row, avoiding repetition of long symbol identities in every index. The FTS5
table is contentless and uses trigram tokenization for substring routing, so indexed
terms do not retain a second copy of source text.
Incremental auto-vacuum bounds free-page growth after changed or removed graph rows.

### 9.1 Portable graph shape

```json
{
  "schemaVersion": 2,
  "build": {
    "id": "sha256:...",
    "repositoryId": "orders",
    "head": "abc123",
    "dirty": true,
    "status": "complete"
  },
  "nodes": [
    {
      "key": "orders::reference-item::api-operation/orders-api%3Aget%3Aorders-id",
      "kind": "reference-item",
      "subtype": "api-operation",
      "localId": "api-operation/orders-api%3Aget%3Aorders-id",
      "label": "GET /orders/{id}",
      "facets": ["contract"],
      "authority": "canonical",
      "lifecycle": "active",
      "locations": [],
      "properties": {},
      "provenance": []
    }
  ],
  "edges": [],
  "diagnosticSummary": {
    "errors": 0,
    "warnings": 0
  }
}
```

`manifest.json` carries a human-inspectable input and extractor inventory.
`diagnostics.json` mirrors build diagnostics. Both are reproducible from the canonical
inputs and the database; the database holds the queryable copies used by graph
commands. The portable shape above is emitted by `cis graph export --type json`.

## 10. Query contract

Queries are read-only and support human, JSON, and agent output. Results report graph
schema/build, baseline/freshness, node identity/type/authority/lifecycle, traversed
edge direction/state/confidence, locators/provenance, filters/depth/limit, truncation,
and relevant diagnostics.

Default traversal includes `declared`, `discovered`, and `confirmed`. Proposed,
rejected, and possibly-stale edges require explicit opt-in.

```text
graph find       exact ID, type, subtype, facet, and bounded text lookup
graph related    bounded neighbourhood traversal
graph trace      bounded path discovery between two nodes
context contract governing docs, owners, implementation, consumers, and tests
context tests-for tests connected to a target with evidence and confidence
context pack     budgeted source selection for a task or change
```

Traversal is cycle-safe, depth/result-limited, and deterministic. Output never
implies completeness when extractors or inputs are partial.

Graph export is read-only with respect to canonical inputs. It produces deterministic
Markdown, JSON, JSONL, or Graphviz DOT. Default artifacts remain disposable beneath
`.cis/local/`; custom output paths must remain inside the repository and cannot replace
manifest inputs or graph generation files.

## 11. Context-pack boundary

A context pack is a derived selection from graph nodes plus canonical content. It is
not stored in the graph database; it lives under `.cis/local/context/`.

Every pack reports purpose/start nodes, graph build and repository baseline, included
sources and selection reasons, omissions/truncation, estimated size, strict versus
interpreted facts, diagnostics/uncertainty, and requested verification evidence.

Pack selection resolves graph locators before reading content. Line, heading,
table-row, and declaration locators produce bounded excerpts; whole-file fallback is
allowed only when no precise locator resolves. Source manifests retain contributing
node keys, relationships, state, confidence, locator, and extraction method. Character
budgets also report a deterministic approximate token count.

Known credential paths, key material, binary or oversized inputs, and recognized
literal secrets are omitted before rendering. Findings identify only the detection
rule and path; matched values are never copied into output or diagnostics.

The goal is the smallest reliable context for the task, not a repository dump.

A pack may contain up to 20 exact roots across one or more initialized repositories.
Federation occurs at query time: every repository retains an independent graph build,
baseline, freshness state, diagnostics, node identity, and source paths. Repository
IDs qualify every selected source and omission. Explicit roots establish the federation
boundary; CIS does not infer cross-repository relationships merely because names or
dependencies resemble one another. Budget allocation is interleaved by relevance rank
across roots, and output is written only in the primary repository.

Multi-repository workspaces record initialized repositories in the canonical
`.cis/workspace.yml` registry. `cis repo import` reconciles repository initialization
and registry membership without copying sources. `cis graph build --workspace` and
`cis graph validate --workspace` apply the graph lifecycle independently to every
registered repository and expose every partial or failed result. The registry is a
selection and identity boundary, not a merged graph and not evidence for an edge.
`cis workspace init` assigns exactly one documentation repository the `authority` role;
imports use `participant`. Legacy entries without a role remain compatible participants.
Repository roles describe canonical ownership and never create graph relationships.

## 12. Validation and health

`cis graph validate` reports:

- duplicate identities or aliases;
- missing, escaping, or unresolvable paths;
- unsupported node, facet, edge, state, or authority values;
- broken endpoints or invalid source/target kinds;
- reference rows without governed stable IDs;
- ambiguous symbol/test identity;
- contradictory documentation and implementation observations;
- stale hashes, declarations, or graph baseline;
- proposal/confirmation without a durable source record;
- sensitive values copied into graph properties; and
- local graph artifacts tracked by Git.

Errors prevent replacing the last healthy graph. Warnings remain visible in queries,
doctor output, and context packs. The graph module will contribute graph health to
`cis repo doctor` through the modular doctor-check contract.

## 13. Initial acceptance scenarios

### 13.1 API change path

Given an API row, CIS can navigate:

```text
API operation
-> governing specification
-> owning component
-> implementation symbol
-> consumers
-> permissions and problems
-> verifying tests
```

Every hop reports state, confidence, and evidence.

### 13.2 Event path

Given an event ID, CIS finds its producer, consumers, triggering command,
projection/read model, and related tests without relying on text search alone.

### 13.3 Requirement path

Given a requirement ID, CIS finds its specification, implementation, related
contracts, and verification. Missing mappings are gaps, not invented edges.

### 13.4 Document path

Given a catalog ID, CIS opens canonical Markdown, lists declared/discovered related
nodes, and distinguishes proposals from confirmed relationships.

### 13.5 Rebuild and disposal

Deleting `.cis/local/graph/` and rebuilding an unchanged repository produces the
same semantic nodes, edges, and build ID. No canonical file is modified.

## 14. MVP boundary

MVP includes federated local repositories; repository, component, document, reference-item,
source-file, symbol, invocation, dataflow, test, workflow, and dependency nodes; deterministic catalog,
Markdown, project, source, test, and workflow extraction; registered edge types; JSON
storage and validation; exact/type/facet lookup and bounded traversal; multi-root
query-time federation; compiler-bound C# calls; and
evidence-rich human, JSON, and agent output.

Non-goals include vector search, graph databases, automatic semantic-edge acceptance,
model-generated canonical content, unbounded traversal, and graphical layout. Compiler
binding is adapter-specific: C# is compiler-backed, while other language adapters keep
their deterministic lexical or structured behavior until equivalent front ends exist.

## 15. Implemented slices

`cis graph build` includes repository, catalog-document, repository-profile component,
governed reference-item, source-file, symbol, test, workflow-job, and dependency nodes.
It hashes every supported canonical and implementation input and emits evidence-backed
`contains`, `describes`, `declares`, `governed-by`, `owns`, `belongs-to`, `depends-on`,
`calls`, `implemented-by`, and `verified-by` edges with content-addressed build identity.

Canonical change-decision ledgers create `decision` nodes carrying category,
blocking/advisory gate, lifecycle, resolution, and promotion evidence. Catalogued ADRs
also create canonical architecture-decision nodes. A promoted ADR has a confirmed
`generated-from` relationship to its originating change-local decision.

Language extraction retains deterministic lexical adapters and adds Roslyn semantic
binding for C#. Compiler-bound declarations carry stable qualified signatures,
attributes, effective interfaces, base-type inheritance, generated-code reasons, and high-confidence `calls`
evidence for repository and external symbols. Every resolved call is represented by a
content-addressed `calls` edge. A separate `invocation` node is retained only when
generic/semantic argument shape or recognized flow ordering must remain independently
addressable; raw argument values are never persisted. Adjacent semantic invocation order and recognized persistence, transaction, outbox, publication,
and idempotency milestones produce an intraprocedural `dataflow` summary. This is not
interprocedural control-flow or runtime proof. Recognized test methods also call their
bound production symbols. Project references prove dependency only. A deterministic
verification relationship requires a recognized test case containing the exact stable
identity of a governed reference; an exact declared evidence path may connect that
reference to its source file.

The first query slice includes exact/type/subtype/facet/bounded-text lookup, bounded
cycle-safe neighbourhood traversal, explicit proposal opt-in, input/extractor
freshness checks, truncation reporting, and human/JSON/agent output. Focused context
consumers retrieve immediate contract, symbol, and `verified-by` test evidence without
defining competing graph semantics.

Stored-generation validation covers schema/build identity, registered node and edge
semantics, identities, endpoints, evidence and repository-safe locators, manifest
freshness, proposal/confirmation provenance, potential sensitive properties, retained
build diagnostics, and accidental Git tracking of local artifacts. Warnings remain
policy-enforceable through strict mode without changing or rebuilding the graph.

The graph command surface is complete: build, validate, find, related, trace, and
export. Trace returns deterministic equal-length shortest paths with direction,
relationship state, confidence, evidence, proposal opt-in, and explicit bounds.
Export produces safe idempotent Markdown, JSON, JSONL, and Graphviz representations.

The context command surface now includes search, contract, symbol, references,
callers, tests-for, and pack. Focused queries preserve graph evidence and make their
bounded relationship semantics explicit. Context packs select canonical source
locations deterministically, classify strict versus interpreted evidence, preserve
freshness and diagnostics, enforce graph and character budgets, and write idempotent
Markdown only beneath `.cis/local/context/`. Locator-aware excerpts prevent a relevant
symbol or table row from consuming budget on an unrelated whole file. Per-source budget
fairness prevents one highly connected file from exhausting the entire pack. Duplicate
endpoint observations are compacted, and the manifest reports total, shown, and omitted
evidence counts. Pack manifests
carry node, edge, state, confidence, locator, method, character, and estimated-token
evidence. Sensitive-path and literal-secret guards exclude unsafe content without
echoing detected values.

Context packs support multiple exact roots in one repository and query-time federation
across independently initialized repositories. Root-specific build identity, baseline,
freshness, diagnostics, node keys, and source paths remain visible in every output.
Relevance-ranked source allocation is interleaved across roots, and only the primary
repository receives the derived Markdown artifact.

Workspace import, listing, bulk graph build, and bulk validation remove the need to
maintain repository path lists outside CIS while retaining independent graph files and
query-time federation semantics.

Proposal generation/review belongs to the AI and assurance workflow layers. Deeper
semantic contradiction analysis and model-assisted ranking remain separate assurance
concerns; deterministic multi-root, cross-repository, and compiler-backed C# analysis
are part of the implemented context model.

Per-file index cards complement rather than extend the canonical graph. They live
under `.cis/local/index-cards/`, retain source hashes and model provenance, and support
low-token routing through `cis index find`. Card summaries do not create nodes or
edges, prove impact, or replace source inspection. The graph remains deterministic;
model-assisted routing remains disposable and independently refreshable.

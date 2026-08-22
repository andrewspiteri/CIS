---
title: "Change Impact Studio Documentation Inventory and Validation"
type: specification
status: Draft
version: "0.1"
scope: "Product:ChangeImpactStudio"
owner: "Andrew Spiteri"
last_reviewed: "2026-08-04"
review_cadence: "on change"
---

# Documentation Inventory and Validation

## 1. Purpose

This specification defines the first `docs` module slice:

```text
cis docs inventory
cis docs validate
```

The slice establishes a deterministic view of existing Markdown before CIS proposes,
generates, or changes documentation. It does not build the documentation graph and
does not mutate canonical files.

## 2. Source-of-truth rules

- Markdown beneath the configured documentation root remains canonical.
- `<documentation-root>/catalog.yml` carries stable document identity and
  classification.
- `.cis/repository.yml` selects exactly one repository-relative documentation root.
- Inventory results are observations, not a parallel documentation store.
- Inference is allowed when deterministic and is labelled `inferred`.
- Missing or ambiguous classification is reported; it is not silently applied.

These rules adapt the transferable PARR pattern of canonical Markdown, stable IDs,
typed metadata, and disposable indexes without adopting PARR-specific document types
or business concepts.

## 3. Repository context

Both commands accept:

```text
--repo <path>
--format human|json|agent
```

`--repo` defaults to the current directory. Commands resolve the documentation root
from `.cis/repository.yml`; callers do not repeat `--root`.

Resolution fails when configuration is missing or malformed, its schema is
unsupported, the repository ID is missing, or the documentation root is absolute,
outside the repository, or absent.

## 4. Catalog schema

The MVP catalog shape is:

```yaml
schema_version: 1
repository: example
documents:
  - id: example:spec:delivery
    path: docs/specs/delivery.md
    type: technical-specification
    status: draft
    authority: canonical
```

Required document fields are:

| Field | Meaning |
|---|---|
| `id` | Stable, case-insensitively unique identity |
| `path` | Repository-relative Markdown path inside the documentation root |
| `type` | Repository-defined document classification |
| `status` | Lifecycle status |
| `authority` | `canonical`, `routing`, `derived`, or `proposal` |

Document types and statuses remain repository-extensible in this slice. Graph node
types and lifecycle vocabularies will be introduced by their owning modules rather
than embedded prematurely in catalog validation.

## 5. Inventory

```bash
cis docs inventory --repo . --format json
```

Inventory recursively discovers `*.md` beneath the configured root and reports:

- stable or deterministically inferred ID;
- repository-relative path;
- title;
- type and status;
- authority;
- metadata source;
- whether the document is cataloged;
- front-matter presence and parse state.

Metadata precedence is:

1. catalog entry;
2. explicit front matter, including optional `cis.stable_id` metadata;
3. deterministic inference from repository ID and path.

Title inference uses the first level-one heading and then the filename. Type inference
uses the standard `architecture/decisions`, `architecture`, `specs`, `references`,
and `changes` folders before falling back to `document`.

## 6. Validation

```bash
cis docs validate --repo .
cis docs validate --repo . --strict
```

Validation detects errors for:

- malformed or unsupported catalog schema;
- missing required catalog fields;
- duplicate IDs or paths after case normalization;
- unsupported authority values;
- absolute paths, repository escapes, or paths outside the documentation root;
- missing or non-Markdown catalog targets;
- malformed YAML front matter in cataloged documents.

Validation reports warnings for:

- Markdown files not represented in the catalog;
- cataloged documents without YAML front matter;
- other non-fatal inventory ambiguity.

Warnings do not fail normal validation. `--strict` promotes the presence of any
warning to a validation failure without changing the underlying classification.

## 7. Output and exit codes

Human output is readable, JSON output is structured, and agent output is stable
line-oriented data. Diagnostics use standard error; command results use standard
output.

| Exit code | Meaning |
|---|---|
| `0` | Inventory completed or documentation is valid under the selected strictness |
| `2` | Invalid repository context, command input, or output format |
| `5` | Catalog, document metadata, or strict documentation validation failed |

## 8. Deferred behavior

This slice does not:

- write or update catalog entries;
- add front matter;
- generate replacement documents;
- validate Markdown links or graph edges;
- build cards, indexes, graphs, or context packs;
- classify whether existing documents should be kept, extended, linked, or replaced.

Those capabilities belong to later `docs propose/apply`, `graph`, and `context`
slices and must preserve the same no-silent-mutation boundary.

## 9. Acceptance criteria

- Both commands read the root exclusively through repository configuration.
- Inventory finds nested Markdown deterministically.
- Catalog metadata takes precedence over inference.
- Uncataloged Markdown remains visible.
- Duplicate identities and missing catalog targets fail validation.
- Existing Markdown is never modified.
- Normal validation distinguishes errors from warnings.
- Strict validation fails when warnings remain.
- Human, JSON, and agent formats expose equivalent facts.

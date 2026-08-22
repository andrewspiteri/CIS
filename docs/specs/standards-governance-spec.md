---
title: Standards Governance Specification
type: governance-specification
status: Active
owner: Repository maintainer
review_cadence: on change
cis:
  stable_id: change-impact-studio:spec:standards-governance
---

# Standards Governance Specification

## Intent

Standards define expected ways of working through testable, stable rules. Markdown below `docs/standards/` is canonical; command output and graph or index state are derived routing aids.

## Document contract

Each standard must declare `title`, `type: standard`, `status`, `targets`, optional `stacks`, `owner`, `last_reviewed`, `review_cadence`, `source_of_truth`, and `cis.stable_id`. Required sections are Purpose, Scope, Normative language, Rules, Verification, and Exceptions. Active and draft standards require stable rule IDs.

Lifecycle statuses are `Draft`, `Active`, `Deprecated`, and `Archived`. Stable IDs and rule IDs are immutable; retired IDs are never reused. A standard becomes applicable by target and, when declared, stack. `generic` and `all` stacks match every stack.

## Command behavior

- `cis standards inventory` lists cataloged standards and supports target, stack, and status filters.
- `cis standards applicable` requires at least one target and returns matching active standards.
- `cis standards validate` checks location, catalog authority, metadata, sections, stable rule uniqueness, lifecycle, and conformance coverage. `--strict` turns warnings into failures.
- `cis standards conformance` reads the canonical matrix; `--gaps-only` isolates unmapped, advisory-only, or inactive mappings.
- `cis standards import` accepts local Markdown files/directories, ZIPs, GitHub repository/tree URLs, and direct ZIP URLs. It validates staged copies, supports explicit safe structural `--fix`, may assign stable IDs to existing normative statements but refuses to invent semantics, requires `--yes` for canonical changes, and atomically adds catalog entries plus conservative `manual-review` mappings.
- `cis standards audit` detects deterministic duplicate rules and bodies, bounds semantic pair review, prefers a local model, falls back to configured remote generation unless `--no-llm` is supplied, and persists derived evidence below `.cis/local/standards/`.
- `cis standards audit --fix` may move redundant deterministic duplicates and evidence-backed conflicts to `standards-quarantine/`. It changes their catalog entries to historical quarantined records and preserves former matrix rows as non-active quoted history; it never merges rules or approves a resolution.
- `cis standards patterns` inventories and validates versioned built-in, extension-provider, and canonical repository pattern definitions. Duplicate stable IDs are conflicts and are never resolved by provider load order.
- `cis standards infer` requires a fresh compiler-backed graph, evaluates only applicable patterns whose required graph capabilities are present, preserves matches and counterexamples below `.cis/local/standards/inference/`, and emits unreviewed candidates without changing canonical standards.
- Commands support human, JSON, and compact agent output.

## Conformance model

Every active rule has a canonical matrix row with standard ID, rule ID, surface, enforcement, evidence, lifecycle status, and notes. Allowed enforcement states are:

| State | Meaning |
|---|---|
| `deterministic` | A reproducible evaluator can establish pass or fail for the mapped rule. |
| `architecture-test` | A structural test enforces the mapped rule. |
| `manual-review` | Named human review and recorded evidence are required. |
| `advisory-model` | A local or remote model may identify candidates but cannot prove a breach or compliance. |
| `not-mapped` | No enforcement route exists; this is a visible governance gap. |

## Authority and exceptions

Tools and agents may inventory, route, validate, and propose remediation. Only the named human authority may accept an exception or decide that advisory evidence is a confirmed finding. Exceptions record rule ID, approver, rationale, bounded scope, expiry or review condition, and compensating controls.

## Initialization and reconciliation

`cis repo init` creates the standards directory and idempotently seeds this specification, the documentation governance standard, a classification-selected PARR-derived default set, standard and inference-pattern templates, the conformance matrix, and agent guidance. Defaults preserve source provenance and omit product-specific PARR assumptions. Existing human changes are preserved by managed-starter collision rules.
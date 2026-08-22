---
title: Documentation Governance Standard
type: standard
status: Active
targets:
  - documentation
  - repository-governance
stacks:
  - generic
owner: Repository maintainer
last_reviewed: 2026-08-15
review_cadence: on change
source_of_truth: This file
cis:
  stable_id: change-impact-studio:standard:documentation-governance
---

# Documentation Governance Standard

## Purpose

Define the required structure, authority, lifecycle, and verification expectations for repository-owned standards.

## Scope

This standard applies to every document cataloged with `type: standard`, its stable rules, its conformance mappings, and agent or human work governed by those rules.

## Normative language

`MUST` and `MUST NOT` are mandatory. `SHOULD` requires recorded rationale when not followed. `MAY` is optional. A policy defines authority or mandatory outcomes; a standard defines the expected way of satisfying them; a specification defines a precise product or tool contract; a procedure describes ordered execution steps.

## Rules

- **CIS-STD-DOC-001** A standard MUST be canonical Markdown below the configured `standards/` directory and MUST be registered in `catalog.yml` with `type: standard` and `authority: canonical`.
- **CIS-STD-DOC-002** A standard MUST declare title, lifecycle status, targets, owner, review date, review cadence, source of truth, and an immutable `cis.stable_id` matching its catalog ID.
- **CIS-STD-DOC-003** Every normative requirement MUST use a repository-unique stable rule ID. Retired rule IDs MUST NOT be reused.
- **CIS-STD-DOC-004** A standard MUST distinguish mandatory, recommended, and optional expectations using the normative language defined above.
- **CIS-STD-DOC-005** Every active rule MUST have a row in the standards conformance matrix, even when its current enforcement is manual review.
- **CIS-STD-DOC-006** Advisory model or heuristic evidence MUST NOT be reported as a confirmed breach or proof of compliance without deterministic evidence or explicit human review.
- **CIS-STD-DOC-007** Exceptions MUST name the approving authority, rationale, scope, expiry or review condition, and compensating controls. An agent MUST NOT approve its own exception.
- **CIS-STD-DOC-008** A relevant standard MUST be resolved before governed implementation begins and changed standards MUST be validated before completion.
- **CIS-STD-DOC-009** Imported standards MUST be validated in staging, previewed before canonical admission, confirmation-gated, and reconciled with the catalog and conformance matrix without overwriting divergent existing content.
- **CIS-STD-DOC-010** Standards audits MUST preserve deterministic and model provenance. Model findings MUST remain advisory, and automated remediation MUST be limited to reversible quarantine that preserves historical catalog and conformance evidence.

## Verification

Run `cis standards applicable` with the affected targets and stacks, then run `cis standards validate --strict`. Review `cis standards conformance --gaps-only` for unmapped or advisory-only enforcement. After importing or changing the standards collection, run `cis standards audit`; use `--no-llm` when only deterministic comparison is authorized.

## Exceptions

Record approved exceptions in the affected change dossier or ADR and link the rule ID. Absence of automated enforcement is not an exception; map the rule to `manual-review` until stronger enforcement exists.

## Related documents

- `../specs/standards-governance-spec.md`
- `../references/standards-conformance-matrix.md`
- `../templates/standard-template.md`
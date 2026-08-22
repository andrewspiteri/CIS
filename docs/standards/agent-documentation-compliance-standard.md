---
title: "Agent Documentation Compliance Standard"
type: standard
status: Active
targets:
  - documentation
  - repository-governance
owner: Repository maintainer
last_reviewed: 2026-08-15
review_cadence: on change
source_of_truth: This file
provenance:
  method: curated adaptation
  source_repository: PARR
  source_paths:
    - docs/generic/standards/agent-documentation-compliance-standard.md
cis:
  stable_id: change-impact-studio:standard:agent-documentation-compliance
---

# Agent Documentation Compliance Standard

## Purpose

Keep agent-authored changes aligned with canonical repository documentation and make conflicts visible to human reviewers.

This starter is a classification-safe adaptation of the listed PARR standards. Repository maintainers may strengthen it, record bounded exceptions, or supersede it through reviewed canonical changes.

## Scope

Applies to: documentation, repository-governance.

## Normative language

`MUST` and `MUST NOT` are mandatory. `SHOULD` is the expected default and requires recorded rationale when not followed. `MAY` identifies an allowed option.

## Rules

- **AGENT-DOC-001** Agents MUST resolve and read the canonical specifications, standards, references, decisions, and instructions applicable to a change before modifying implementation.
- **AGENT-DOC-002** Agents MUST treat repository documentation as canonical when derived graph, index, or model output disagrees with it.
- **AGENT-DOC-003** Agents MUST report conflicts between a request and canonical repository governance instead of silently bypassing the documented rule.
- **AGENT-DOC-004** Completion evidence MUST identify the governing documents used and any documentation changed with the implementation.

## Verification

- `AGENT-DOC-001`: Review the recorded context package or tool evidence.
- `AGENT-DOC-002`: Review conflict handling and canonical citations.
- `AGENT-DOC-003`: Review change evidence and decision records.
- `AGENT-DOC-004`: Review final delivery evidence and documentation traceability.

Run `cis standards validate --strict` after changing this standard or its conformance mappings.

## Exceptions

An exception requires the rule ID, human approver, rationale, bounded scope, review or expiry condition, and compensating controls. CIS and agents cannot approve exceptions.

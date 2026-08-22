---
title: "Workspace Technical Intent Governance"
type: governance-specification
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-20"
review_cadence: "on technical-intent lifecycle change"
cis:
  stable_id: change-impact-studio:spec:technical-intent-governance
---

# Workspace Technical Intent Governance

## Authority and sequence

A multi-repository workspace has one canonical, workspace-scoped technical intent at
`<authority-documentation-root>/specs/technical-intent-spec.md`. Participant repositories
retain repository-scoped intent documents as supporting evidence. Technical intent follows
an Active/current BRD and precedes change-dossier creation, impact analysis, and planning.

## Managed evidence

CIS owns only the marked technical-intent baseline and lifecycle metadata. The baseline
records the canonical BRD content hash and every participant graph build ID. Human-authored
technical sections remain canonical. Initialization is idempotent, preserves those sections,
and refreshes managed evidence. Semantic BRD drift or a change to the registered participant
set resets approval. New build IDs for the same participants update provenance without
invalidating the approved technical direction.

## Completeness and decisions

Validation requires workspace scope/schema; business baseline; principles; technical
surface and ownership; runtime/trust boundaries; data and consistency; API/integration/
compatibility; security/privacy; operations/migration/recovery; quality/verification;
delivery constraints; and structured technical decisions. Decisions use stable `TI-DEC-*`
IDs with a required gate, `Open`, `Proposed`, `Resolved`, `Accepted`, or `Deferred` status,
and rationale. Open and Proposed choices block approval. Deferral cannot cross a declared
change-dossier gate.

## Lifecycle and drift

Effective states are Missing, Review Required, Ready for Approval, Active, and Stale.
Approval requires explicit reviewer identity and rationale and records an approval timestamp
and normalized content digest. Semantic BRD drift, participant-set drift, or approved-content
edits make Active content Stale. Participant build-version drift is reconciled as managed
provenance and does not alone make the technical intent Stale. Automation cannot restore Active.
An unmet upstream readiness condition is a workflow block, not structural corruption:
status remains readable, while initialization and approval return the blocked exit code.

## Downstream gate

`cis change create`, `cis plan build`, and `cis plan import-spec` evaluate registered
readiness checks. In a workspace authority, an Active/current technical intent is mandatory.
Standalone and participant repositories without a local workspace authority marker remain
outside this workspace gate.

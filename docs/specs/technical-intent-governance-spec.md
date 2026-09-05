---
title: "Workspace Technical Intent Governance"
type: governance-specification
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-05"
review_cadence: "on technical-intent lifecycle change"
cis:
  stable_id: change-impact-studio:spec:technical-intent-governance
---

# Workspace Technical Intent Governance

## Authority and sequence

A product workspace has one canonical, workspace-scoped technical intent at
`<authority-documentation-root>/specs/technical-intent-spec.md`. Participant repositories
retain repository-scoped intent documents as supporting evidence. Product-owned
participants supply implementation evidence; dependencies supply only bounded integration
context. Technical intent follows
an Active/current BRD and a governed high-level technical questionnaire, and precedes
change-dossier creation, impact analysis, and planning. The canonical questionnaire lives at
`<authority-documentation-root>/specs/technical-intent-questionnaire.md`.

## Managed evidence and deterministic scaffold

CIS owns the marked technical-intent baseline, business-evidence, questionnaire-evidence,
component-map, product-module architecture, integration-point catalog, technical-surface-evidence,
standards-evidence, and questionnaire-derived decision blocks plus lifecycle metadata. The baseline
records the canonical BRD semantic digest, completed questionnaire digest, every participant graph
build ID, and schema-4 Active standard digests. The other
managed blocks project canonical BRD facts, classifications, and standard provenance. Architecture
decisions and all other human-authored technical sections remain canonical outside those blocks.
Initialization is idempotent, preserves those sections, and refreshes managed evidence. Semantic
BRD or standard drift, or a change to the registered participant/standard set, resets approval.
New build IDs for the same participants update provenance without invalidating approved direction.

Product-owned surfaces determine product modules, frameworks, persistence choices, and
architecture direction. Dependency surfaces are recorded with their declared producer,
consumer, or bidirectional relationship and optional component scope. They generate
explicit integration points but never cause a dependency's framework, database, or
architecture to be inferred as the product's direction. Modifying a dependency requires a
separate change under its owning product workspace.

The questionnaire contains 16 stable `TI-Q-*` decisions covering product surfaces, frontend and
backend technology, architecture and repository topology, primary/supporting data stores,
contracts, identity, hosting, asynchronous work, operations, security/privacy/compliance,
verification, AI/model lifecycle, and explicit constraints or exclusions. For an existing
implementation, deterministic repository classification and bounded manifest/configuration analysis
may resolve evidence-supported facts as `Derived`, with confidence and exact provenance. A human
may override any derived direction. Greenfield and ambiguous choices remain unanswered; suggested
text alone is never governed input. The questionnaire is complete only when every choice is derived
or recorded with human identity and timestamp.

For a new or recognizably generated Draft starter, initialization generates a schema-4 scaffold from
the completed/current questionnaire, Active BRD, registered repository/component classifications,
and Active standards. It carries
forward BRD requirement IDs and outcome context; records technical surface and ownership; adds
standard identities, rule IDs, paths, and content digests; proposes classification-selected
architecture guidelines; generates logical component responsibilities and primary interactions;
derives stable `TI-MOD-*` product-module candidates and detailed responsibility profiles from the
BRD's business-capability section; and generates stable `TI-INT-*` cross-boundary handoffs from those
modules, known external-system signals, and the governed contract, identity, data, asynchronous, and
operations choices;
and binds accepted `TI-DEC-*` records to the exact governed answers and their human or repository
provenance. Active standards and the
questionnaire are schema-4 baselines, so changed content or membership
requires review. CIS replaces only recognized starter sections and never overwrites a substantive
human-authored section. Existing reviewed schema-1 through schema-3 documents remain compatible;
generated Draft schema-2 and managed Draft schema-3 starters are upgraded once the questionnaire is complete.

## Completeness and decisions

Validation requires workspace scope/schema; business baseline; principles; technical
surface and ownership; runtime/trust boundaries; data and consistency; API/integration/
compatibility; security/privacy; operations/migration/recovery; quality/verification;
delivery constraints; and structured technical decisions. Decisions use stable `TI-DEC-*`
IDs with a required gate, `Open`, `Proposed`, `Resolved`, `Accepted`, or `Deferred` status,
and rationale. Open and Proposed choices block approval. Deferral cannot cross a declared
change-dossier gate.

Schema 2 additionally requires a non-placeholder architecture-guidelines and applicable-standards
section with visible provenance. Generated guidelines are Draft direction until the technical
intent is explicitly approved.

Schema 3 additionally requires high-level technology and architecture choices, a logical component
and interaction map, and managed provenance for the questionnaire-derived decisions. Completing
the questionnaire resolves the pre-intent decision boundary, but does not approve the resulting technical
intent; one technical review and approval remains required.

Schema 4 additionally requires a product-module architecture and integration-point catalog. Module
profiles record purpose, BRD authority, ownership and exclusions, inputs, outputs, data/state,
security, failure/recovery, and verification direction. Integration records identify source, trigger,
target, contract/data, delivery/consistency, trust/authorization, failure/recovery, and BRD authority.
These are reviewed architecture candidates, not invented deployables or implementation endpoints;
feature design may refine them only with preserved traceability and explicit ownership migration.

## Lifecycle and drift

Effective states are Missing, Review Required, Ready for Approval, Active, and Stale.
Approval requires explicit reviewer identity and rationale and records an approval timestamp
and normalized content digest. Semantic BRD drift, participant-set drift, or approved-content
edits make Active content Stale. Questionnaire-answer drift also makes the technical intent Stale.
Participant build-version drift is reconciled as managed
provenance and does not alone make the technical intent Stale. Automation cannot restore Active.
An unmet upstream readiness condition is a workflow block, not structural corruption:
status remains readable, while initialization and approval return the blocked exit code.

## Downstream gate

`cis change create`, `cis plan build`, `cis plan import-spec`, and `cis plan derive` evaluate registered
readiness checks. In a workspace authority, an Active/current technical intent is mandatory.
Standalone and participant repositories without a local workspace authority marker remain
outside this workspace gate.

After delivery changes participant graph identities, `cis technical-intent refresh`
reconciles the BRD, technical-intent baselines, and high-level backlog in order. Existing
human authority carries forward only when canonical semantic content, source assessments,
technical decisions, and backlog outcomes remain unchanged. Pure managed-baseline drift
does not require duplicate approval. A material or unresolved change stops at its owning
stage and requires review of that difference.

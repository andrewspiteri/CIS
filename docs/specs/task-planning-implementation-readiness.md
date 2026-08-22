---
title: "Task Planning Implementation Readiness"
type: gap-analysis
status: Draft
version: "0.1"
scope: "Product:ChangeImpactStudio"
owner: "Andrew Spiteri"
last_reviewed: "2026-08-13"
review_cadence: "on planning implementation change"
cis:
  stable_id: change-impact-studio:analysis:task-planning-implementation-readiness
---

# Task planning implementation readiness

## 1. Purpose

This review compares the CIS task-planning specification and current implementation
with the PARR lead-research feature specification, issue pack, execution ledger,
screen-rendering source, and design approval process. It separates specification
gaps from implementation work so the first implementation slice can begin without
claiming that later task types are already fully defined.

## 2. PARR capabilities covered by the specifications

| PARR pattern | CIS specification status |
| --- | --- |
| Rich narrative feature specification | Covered: native and PARR-shaped documents, numbered goals, requirements, references, targets, and exclusions. |
| Parent issue and decomposed child pack | Covered by Coordination / scope guard, durable task instances, dependencies, complexity, and decomposition rules. |
| Per-issue objective, behavior, exclusions, acceptance, and targeted tests | Covered by the common task-type and task-instance contracts. |
| Plan/issue ledger | Covered as a canonical dependency/status ledger with detailed Markdown task documents. |
| Textual screen behavior before visual design | Covered by `wireframes.md`, screen/state definitions, action conditions, side effects, and concrete paths. |
| Reproducible PNG screen pack | Covered by one self-contained JavaScript-generated SVG renderer using pinned Sharp/libvips. |
| Human screen approval before implementation | Strengthened: completed design enters a global work pause until explicit approval. |
| Rejected visual direction and revised pack | Covered through renderer revision, rejected PNG hashes, findings, rationale, and resubmission while the pause remains active. |
| Documentation/contracts/dictionaries | Implemented in the core task-type catalog and registry. |
| Data, API, permission, handoff, carry-forward, tests, and final sweep | Implemented in the core task-type catalog and registry. |
| Product-specific search/projection | Correctly moved to an extension task-type provider rather than CIS core. |
| Tool/evidence ledger | Host logging and automatic task/verification completion snapshots are implemented. |
| Final planned-versus-actual review | Implemented by Coordination acceptance and Final delivery sweep. |

## 3. Integration boundaries intentionally kept outside this slice

The canonical nineteen-type workflow and repository delivery policies are now
defined. Two adapter implementation boundaries remain:

1. Extension registration, capability conflicts, canonical selection persistence,
   explicit replacement, and evidence-preserving instance migration are implemented.
   Removal of a loaded provider is diagnosed as unavailable selection/task state;
   automatic provider installation remains outside planning.
2. The provider-neutral synchronization contract and host define identity, field
   authority, direction, conflict, deletion, rate-limit, authorization, offline/retry,
   durable-link, and local-state behavior. Separately loaded GitHub Issues and Jira Cloud
   transport assemblies now implement that contract and pass offline HTTP conformance tests.

## 4. Implemented gap closure

The reviewed implementation now provides:

1. A provider-backed registry with nineteen ordered core definitions and duplicate-key rejection.
2. Evidence-driven applicability, natural dependencies, and the direct global design dependency for every downstream UI task.
3. Textual wireframe and visual design records, approvals/rejections, and an enforced global pause.
4. A design module for reusable templates, scaffolding, static validation, Sharp/SVG rendering, artifact hashing, review, and status.
5. A shared application shell and standard components, including tables, with versioned possible token-saving estimates.
6. Populated default design guidelines, a generic guideline template, and design agent guidance during idempotent initialization.
7. Structured task type/version, targets, decisions, exclusions, outputs, gates, evidence, and lifecycle transitions.
8. Evidence-preserving idempotent feature-spec re-import.
9. Plan validation for ordering, direct global barriers, wireframe/design records, type identity, decisions, coverage, and lifecycle.
10. Completion-time snapshots from the local tool-usage ledger into canonical task and verification evidence.
11. Individual canonical definitions for all nineteen core task types.
12. Provider conflict/replacement and repository delivery policies, including an
    idempotently seeded target-repository delivery policy with a safe local-only default.
13. Direct Final Delivery Sweep dependencies on every applicable task and enforced
    Coordination-only final human acceptance after the sweep and all child dispositions.
14. External tracker projection, durable remote identity links, three-way drift
    detection, conflict persistence, explicit human resolution, repository doctor
    findings, provider registration, seeded profiles/guidance, and offline conformance tests.

## 5. Implemented sequence

The canonical registry, task contracts, planner, dossier, initializer, design module,
completion ownership, provider selection/migration, provider-neutral tracker host,
GitHub/Jira transport providers, documentation, and regression tests are implemented.

## 6. Readiness conclusion

The canonical task-planning, wireframe, design-template, rendering, approval,
lifecycle, evidence, delivery-policy, and final-acceptance slice is implemented.
External tracker synchronization is now bounded by this canonical model and cannot
silently acquire plan, design, evidence, risk, completion, or acceptance authority.

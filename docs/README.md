---
title: "Change Impact Studio Documentation"
type: navigation
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-25"
review_cadence: on documentation structure change
cis:
  stable_id: change-impact-studio:docs:root
---

# Change Impact Studio documentation

This repository uses `docs` as its documentation root while developing CIS itself.

- `architecture/decisions/` — durable architecture decisions
- `specs/` — product and technical specifications
- `standards/` — normative, testable expectations for governed work
- `references/` — current inventories and contracts
- `changes/` — change dossiers
- `manual/` — command syntax, options, effects, outputs, and exit codes
- `articles/` — explanatory articles and editorial series navigation; not product authority

Reader entry points:

- [`../README.md`](../README.md) — build, run, initialize, and find the right documentation
- [`articles/README.md`](articles/README.md) — published and planned articles about CIS and engineering governance
- [`manual/README.md`](manual/README.md) — command reference organized by module
- [`specs/product-intent-spec.md`](specs/product-intent-spec.md) — current product purpose, scope, principles, requirements, and success measures
- [`specs/system-context-spec.md`](specs/system-context-spec.md) — product boundary, actors, external systems, and authority flow
- [`specs/technical-intent-spec.md`](specs/technical-intent-spec.md) — implementation principles, trust boundaries, and quality attributes

Canonical starting points:

- `standards/documentation-governance-standard.md` — required structure, stable rules, lifecycle, exceptions, and evidence boundaries for standards
- `specs/standards-governance-spec.md` — applicability, validation, conformance, initialization, and authority contract
- `specs/standard-pattern-catalogue-and-inference-spec.md` — compiler-graph pattern contract, known patterns, inference evidence, and authority boundaries
- `references/standards-conformance-matrix.md` — rule-level enforcement routes and visible governance gaps
- `templates/standard-template.md` — reusable starter for repository-specific standards
- `standards/*-standard.md` — classification-selected PARR-derived defaults with canonical provenance and rule-level conformance mappings

- `specs/external-tracker-synchronization-spec.md` - canonical authority, identity, conflict, deletion, retry, credential, and provider rules for external issue mirrors
- `references/external-tracker-profile.md` - disabled-by-default GitHub, Jira, and extension-provider targets and mappings
- `references/ai-routing-profile.md` - local-first model capability routes and remote/cache authority
- `references/diagnostics-profile.md` - bounded runtime evidence sources and sensitivity flags
- `references/learning-history.md` - human-approved applied learning record
- `workflows/standard-delivery.md` - shell-free checkpointed build and test workflow

- `specs/public-endpoint-caching-policy-spec.md` — mandatory caching and persistence isolation for unauthenticated endpoints
- `specs/brd-high-level-backlog-spec.md` — reviewed BRD decomposition into high-level product outcomes before feature specifications and detailed plans
- `specs/api-design-and-governance-spec.md` — PARR-derived API exposure, routing, security, compatibility, inventory, OpenAPI, and drift rules
- `references/api-governance-profile.md` — repository-owned routing, compatibility, OpenAPI, header, and supported-version decisions

- `specs/task-type-contract-spec.md` — common task definition, lifecycle, dependency, evidence, and external-projection boundaries
- `specs/core-task-type-catalog.md` — all nineteen core task types, triggers, ordering, outputs, validation, and gates
- `specs/task-type-extension-policy.md` — provider identity, conflict, explicit replacement, upgrade, and removal rules
- `specs/repository-delivery-policy-spec.md` — branch, commit, push, pull-request, and remote-action authority
- `specs/coordination-scope-guard-task-type.md` — first reviewed core task type and root feature-delivery completeness process
- `specs/wireframe-task-type.md` — textual screen, state, action, and navigation-path contract
- `specs/visual-design-task-type.md` — self-contained JavaScript renderer, PNG screen-pack, validation, and approval contract
- `specs/task-planning-implementation-readiness.md` — PARR comparison, remaining specification gaps, and implementation sequence
- `specs/parr-testing-delivery-gap-matrix.md` — scored whole-process and test-suite comparison against the reusable PARR assurance model
- `specs/parr-functional-parity-gap-matrix.md` — reusable PARR toolkit capability comparison, CIS evidence, and intentional product differences
- `specs/cis-pre-adoption-hardening-review.md` — closed findings, verification evidence, residual boundaries, and the next-project adoption checklist
- `specs/parr-testing-delivery-implementation-plan.md` — ordered CIS tooling and Friends Todo migration plan for closing the assurance gaps
- `templates/design-guidelines-template.md` — governed visual-language and design-token template for target repositories
- `templates/default-design-guidelines.md` — populated default palette, typography, layout, component, and visual-language rules

- `specs/context-model-and-graph-spec.md` — typed nodes, relationships, provenance, local graph storage, and query boundaries
- `specs/file-index-card-spec.md` — incremental model-assisted per-file routing cards, provenance, privacy, and authority boundaries
- `manual/README.md` — command manual index, including design templates and review gates
- `specs/module-catalog-spec.md` — module responsibilities and delivery sequence
- `specs/implementation-roadmap.md` — canonical ten-stage implementation and completion state
- `specs/execution-assurance-and-learning-spec.md` — AI, generation, workflows, agents, verification, diagnostics, and governed learning
- `specs/deterministic-toolkit-evidence-spec.md` — deterministic template applicability, tooling evidence, policy-impact targets, and structured diagnostics
- `specs/vscode-client-spec.md` — thin editor-client boundary over CLI JSON and canonical Markdown
- `specs/cli-and-repository-initialisation-spec.md` — CLI and repository initialization contract
- `specs/documentation-inventory-and-validation-spec.md` — documentation discovery and catalog health contract
- `specs/classification-driven-initialisation-spec.md` — component classification, starter binding, and repeatable reconciliation
- `specs/ui-framework-resolution-spec.md` — evidence-first UI framework preservation and classification-bound defaults

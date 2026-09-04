---
title: "PARR and CIS Functional Parity Gap Matrix"
type: gap-analysis
status: Active
version: "1.0"
scope: "Product:ChangeImpactStudio"
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on PARR toolkit or CIS capability change"
cis:
  stable_id: change-impact-studio:analysis:parr-functional-parity-gap-matrix
---

# PARR and CIS functional parity gap matrix

## Scope

This comparison treats PARR as the proven source of reusable delivery patterns and CIS
as the provider-neutral product. PARR product modules, commercial domain rules, search
projection behavior, deployment topology, and repository-specific GitHub policy are not
CIS parity requirements.

## Matrix

| Capability | PARR reference behavior | CIS implementation | State |
|---|---|---|---|
| Repository classification and initialization | Stack-aware setup and agent guidance | Idempotent multi-component classification, classification-selected references, workflows, standards, skills, and instructions | Covered |
| Context routing and graph | `parrctx`, compiler graph, packs, routing cards | SQLite graph, compiler-backed C# evidence, context packs, cross-repository workspace queries, index cards, and frontend graph augmenters | Covered |
| Non-API reference governance | Dictionary validation and drift checks | Provider-based discovery, preview-first additive reconciliation, inventory, validation, correlation, baseline diff, and doctor integration | Covered |
| CI investigation | GitHub checks, jobs, logs, artifacts, reruns | Provider contract plus GitHub status, checks, runs, jobs, redacted bounded logs, artifacts, diagnosis, reproduction, and confirmed failed rerun | Covered |
| AI gateway and qualification | Local-first routes, evaluation, usage evidence | Provider contract, runtime probe, benchmark, prompt regression, task-class approval, stale detection, route explanation, local-first privacy gates | Covered |
| MCP access | Governed toolkit stdio surface | Fixed-repository read tools plus opt-in, per-call-confirmed mutations delegated to existing services | Covered |
| Artifact retention | Bounded run artifacts and retrieval | Canonical retention profile, inventory, preview, verified archive, retrieve, conflict-safe restore, and delete tombstones | Covered |
| Frontend context | Framework-specific frontend routing | Common provider contract and built-ins for React/Next, Angular, Vue, SwiftUI, Jetpack Compose, and Godot with graph augmentation | Covered |
| Deterministic generation applicability | `parrgen status/applicable` and mandatory decision | `cis generate status/applicable`, ranked reasons, confidence, variables, safe paths, and used/not-applicable/unavailable evidence | Covered |
| Agent tooling evidence | Preflight and handoff validation | Changed-file obligations for routing, generation, reconciled tests, run identity, usage ledger, placeholders, and strict validation | Covered |
| Policy-impact analysis | Explicit policy targets and deterministic handoff artifacts | Explicit targets, bounded candidate classification, strict inference warning, JSON/Markdown derived reports, and human authority boundary | Covered |
| Runtime diagnostics | `parrdiag` JSONL filtering, redaction, analysis, and learning input | Profile doctor, text/JSONL/workflow/browser/container sources, filters, line evidence, redaction, stable fingerprints, analysis, and normalized export | Covered |
| Testing and security assurance | Layered tests, diagnostics, coverage, mutation, SAST and scanners | Canonical suite/security profiles, result adapters, exact test traceability, log instrumentation, coverage/mutation gates, CodeQL, Semgrep, Gitleaks, Trivy, Checkov, and optional local-AI triage | Covered |
| Human governance | Review and acceptance remain human | BRD, technical intent, impact, plan, design, exceptions, assurance, and final acceptance remain canonical human decisions | Covered |

## Intentional product differences

- CIS does not import PARR commercial modules, search/projection implementation, or
  application-specific identity and deployment assumptions.
- CIS uses modular providers and repository classification where PARR can rely on its
  known monorepo shape.
- CIS MCP mutations are disabled by default and separately confirmed; protocol access
  never expands canonical authority.
- Remote CI, remote AI, managed-provider smoke tests, and repository publication remain
  unavailable until their credentials or explicit authority exist. They are not silently
  reported as passed.

## Result

No reusable PARR toolkit capability identified in this review remains absent. Future
changes are detected by rerunning strict documentation, skill, repository-doctor,
reference, frontend, testing, security, graph, package, and release validation rather
than relying on this narrative alone.

## Current validation evidence

The 2026-08-27 parity implementation was verified against the following fresh evidence:

- Windows and clean Linux Release runs passed all 364 .NET tests; the Linux run used the
  pinned .NET 10.0.400 SDK and caches below `/data`. The VS Code extension passed syntax
  validation and all four Node tests on Linux.
- Strict validation passed for 374 catalogued documents, 42 skills, five standards with
  42 rules, all governed references, and all six built-in frontend providers.
- The rebuilt context graph contains 9,739 nodes and 44,935 edges with build identity
  `sha256:d506ad8fda193feb14a3b82b8a801ac9e181698d81d10f0ebbd9789c4ed4cc18`.
  Its identity is expected to change after this evidence section is indexed; the
  recorded counts establish the completed implementation baseline rather than a
  permanently pinned graph identifier.
- Security run `security-cis-hardening-final-r3` passed Trivy filesystem, Gitleaks secret,
  and Semgrep SAST scanning with no normalized findings. Local Ollama summarisation is
  advisory when findings exist and is now bypassed for an empty finding set, preventing
  unsupported model-generated concerns from appearing in a clean report. Raw Semgrep
  parser diagnostics remain visible and are complemented by the clean compiler analyzer
  gate; the portable scan is not presented as exhaustive CodeQL coverage.
- The 0.3.0 NuGet tool and VSIX were built and clean-install validated. Their SHA-256
  digests are `01032a2aae5e8ab3866faf730eac17c6638467fbc46d076d29442aff7ba051b8`
  and `1d15144c619df4acd21e98a25a297dcf239b609739cf4c526f3f89b597f48b56`.
- Remote GitHub CI and publication were not executed: no valid GitHub credential is
  available and the repositories remain private as directed. CIS reports that state as
  unavailable rather than passed.

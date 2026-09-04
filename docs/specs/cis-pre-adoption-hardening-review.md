---
title: "CIS Pre-Adoption Hardening Review"
type: hardening-review
status: Active
version: "1.0"
scope: "Product:ChangeImpactStudio"
owner: "Andrew Spiteri"
last_reviewed: "2026-08-28"
review_cadence: "before adoption by a materially different project or CIS release"
cis:
  stable_id: change-impact-studio:review:pre-adoption-hardening
---

# CIS pre-adoption hardening review

## Decision

CIS 0.3.0 is ready for private adoption by another project after the gates in this
review pass on the final source revision. Publication remains intentionally disabled.
The target repository must still supply its own human-reviewed product, API, security,
and delivery decisions; successful initialization is not approval of generated Draft
content.

## Review scope

The review covered CLI and MCP trust boundaries, filesystem and archive safety,
external process and network bounds, evidence integrity, initialization and repeated
initialization, graph and index isolation, result adapters, security scanners,
packaging, editor integration, Windows execution, and a clean Linux build.

## Findings closed

| Area | Closed finding | Evidence |
|---|---|---|
| Paths and recursion | Repository-relative inputs use platform-aware containment, reject reparse-point escapes, and bound recursive traversal. | Safety unit and mutation suites |
| Archive handling | Imported ZIPs reject traversal, duplicates, links, oversized entries, cumulative overflow, and existing targets. | `Cis.Abstractions.Tests` |
| Processes | Child processes have bounded output, deterministic timeouts, and process-tree termination. | `CisProcessSafety` tests |
| Remote input | Skill and standard URL imports require HTTPS, reject embedded credentials and downgrade redirects, bound downloads, and extract through the archive boundary. | Skills and standards tests |
| Local evidence | Test, security, CI, diagnostics, and artifact evidence is bounded, hash-addressed where authoritative, redacted, and invalid when an expected report is unreadable or absent. | Module adapter and retention tests |
| Host and MCP | Input size, protocol identity, cancellation, exception disclosure, and exit codes are bounded and tested. | Host and MCP tests |
| CI providers | Response, log, source, and artifact limits are enforced; credentials are scoped and redacted. | CI provider tests |
| Initialization | Classification-selected starters are idempotent, preserve reviewed files, and report collisions. `.codex-tmp`, generated output, dependencies, and release artifacts cannot become repository components. | Repository tests and clean adoption smoke |
| References and frontend | Canonical CIS documents and internal manifests are not rediscovered as application evidence; root routes, package identities, and route reconciliation are deterministic. | Reference and frontend tests |
| Graph and index | Temporary workspaces, dependency trees, release output, and derived state are excluded. The final self-graph is fresh and strict-valid. | Zero graph errors or warnings; zero pending index cards |
| Security | Pinned Semgrep, Gitleaks, and Trivy filesystem scans produce bounded JSON evidence. Trivy configuration scans with no supported target are unavailable rather than passed. Empty finding sets bypass AI triage so a model cannot invent unsupported concerns. | Zero SAST findings, zero secrets, zero blocking dependency findings; no-findings summary regression |
| Distribution | NuGet and VSIX metadata identify the CIS repository, retain the MIT licence and Andrew Spiteri attribution, validate output containment, and share version 0.3.0. | Package install and VSIX smoke tests |

## Verification baseline

| Gate | Outcome |
|---|---|
| Release build | Zero warnings and zero errors |
| Full .NET suite | 411 passed, zero failed, zero skipped, 22 assemblies |
| VS Code extension | 4 passed |
| Unique production-line coverage | 78.48% (29,129 / 37,117), above the 75% inherited baseline; generated `obj/` source is excluded with recorded reason and owner |
| Safety mutation gate | 83.08%, above the 80% break threshold; zero no-coverage mutants |
| Production analyzers | `AnalysisMode=All`, zero warnings and zero errors |
| NuGet audit | Zero vulnerable and zero deprecated packages |
| Documentation | 375 catalogued documents, strict-valid |
| Skills | 42 skills, strict-valid |
| Standards | 5 standards, 42 rules, 42 conformance mappings, strict-valid |
| Testing/security profiles | Strict-valid; two test suites and three applicable security suites |
| Self graph | Fresh and strict-valid; zero errors and warnings |
| Self index | 860 files indexed; zero pending; local Ollama only |
| Repository Doctor | Host-context run completed with zero errors; GitHub authentication and the private `andrewspiteri/CIS` repository were detected; two documentation-readiness warnings remain for human review |
| Clean Linux snapshot | SHA-256 verified; SDK 10.0.400; build clean; 411 .NET and 4 extension tests passed |

Derived reports live under `.cis/local/` and are deliberately untracked. The Linux
verification used an isolated `/data/cis-hardening-final-20260827` snapshot, removed it
after verification, and did not publish source or artifacts.

## Clean adoption smoke

A disposable repository combined ASP.NET Core, Next.js/Vitest, Terraform, and Compose.
Initialization identified the three components, a second run was unchanged, and strict
skills, standards, test-profile, security-profile, references, frontend, graph, and
index checks passed. The final fixture graph contained 128 nodes and 181 edges, and its
local index contained 148 routing cards with zero pending.

The fixture's deliberately incomplete `/health` endpoint remained blocked by API
governance because it lacked a reviewed exposure class and named rate policy. That is
the expected human-governance stop, not an initialization failure.

## Residual boundaries

- The GitHub CI and release workflows have not been pushed or executed. The repository
  remains private on `main`, and GitHub access was verified through the authenticated
  host context. Local commands and a clean Linux equivalent have passed.
- The aggregate 75% coverage floor protects the inherited codebase. New or materially
  changed production code remains subject to the 95% policy; the lower aggregate
  baseline must not be presented as the new-code target.
- The safety mutation gate uses Stryker's Microsoft Test Platform runner, which the
  tool currently labels preview. CIS independently recalculates and hash-binds the
  threshold result.
- Image scanning, DAST, managed identity-provider smoke, and Trivy deployment-
  configuration scanning are not applicable to the CIS CLI repository itself. CIS
  seeds and validates those suites when target-repository classification makes them
  applicable; a missing or zero-target result cannot become a pass.
- Local-AI summaries remain advisory. Scanner JSON, compiler output, test reports,
  hashes, and human decisions retain authority.
- Semgrep reported zero normalized findings but retained non-blocking C# partial-
  parsing and fixpoint diagnostics for newer syntax. The clean compiler analyzer gate
  complements this portable scan; CodeQL should remain enabled when private CI is
  authenticated, and the Semgrep result must not be described as exhaustive SAST.
- Release assembly requires a clean Git worktree by design. During this review the
  NuGet tool and VSIX were built and smoke-tested independently without bypassing that
  release provenance gate.

## Adoption checklist

1. Build and install the verified 0.3.0 package from a clean revision.
2. Run `cis repo init --root <required-root> --yes` in preview/review context and inspect
   classification and collisions.
3. Run `cis repo doctor` after any initialization failure.
4. Validate skills, standards, test and security profiles, references, frontend context,
   graph, and index before starting business-requirement intake.
5. Treat API exposure, identity, secrets, infrastructure, and remote-model choices as
   explicit target-repository decisions.

---
title: "Engineering assurance guidance coverage"
type: reference
status: Draft
owner: "Andrew Spiteri"
last_reviewed: "2026-10-09"
review_cadence: "on onboarding or assurance behavior change"
cis:
  stable_id: change-impact-studio:reference:engineering-assurance-coverage
---

# Engineering assurance guidance coverage

CIS installs portable engineering safeguards during repository initialization and import.
The shared instruction is `.github/instructions/cis-engineering-assurance.instructions.md`.
Security and browser skills point to this guidance, and import supplies its full text as
replacement evidence when reviewing existing instructions. This inventory describes
implemented guidance and checks; it does not approve a repository policy or certify conformance.

| Responsibility | CIS coverage | Repository-specific evidence still required |
| --- | --- | --- |
| Secure features | Backend authorization, principal/tenant isolation, negative tests, production isolation of test authentication, real configuration contracts and secret-safe fixtures | Permission names, principal types, threat model, adopted scanners and actual negative-test results |
| Security evidence | Scanner profiles, strict validation, normalized results, governed exceptions and exact release-image identity | Installed tools, scan outputs, CodeQL or other adopted SAST gates, approved exceptions and remediation ownership |
| Browser regression | Preserve .NET or Node Playwright; deterministic composition, page objects, meaningful action outcomes, shared-control parity, accessibility and failure artifacts | Approved harness, seed data, app setup, exact commands, release ordering and recorded browser results |
| .NET browser discovery | Playwright package markers in test projects produce a browser suite with TRX evidence instead of a solution-wide unit label; browser steps follow discovered lower-level tests/builds | Projects using indirect/shared package references may require profile correction. Browser installation and application setup must use repository-approved commands |
| Other test layers | Unit, component, API/integration, business, architecture, mutation, security and operational obligations remain distinct | Risk-based applicability, real suite boundaries, concrete test commands and execution evidence |
| Contracts and code review | Named API/data/configuration/permission and domain-behavior obligations, review depth, severity and evidence limits | Application contracts, architectural rules, schema/query evidence and exact review findings |
| Scaffolding and diagnostics | Verified inputs, validation, dry-run, intentional overwrite, bounded runtime evidence and privacy | Distinct generators such as ParrGen and telemetry tools such as ParrDiag remain useful when their capabilities are still required |
| Model usage and feedback | Tool-scoped authorization and qualification; preserve distinct usage history, export and retry diagnostics | CIS feedback does not replace another tool's token/cost ledger or its own runtime |
| Connected guidance migration | Discover conventional instruction, skill, agent and prompt files independently of links; show unresolved findings and reversible retirement | Canonical procedures and CI automation are read-only evidence. Their implementation migrations need explicit edits and verification |

Model review is advisory. Two completed passes account for the supplied instruction blocks;
they do not prove every indirect dependency was checked. Read-only excerpts, unreadable
paths, size limits and unresolved findings remain visible. Local automation text matching
finds possible dependencies without executing repository code or transmitting that inventory.

Import can preserve project-specific obligations alongside CIS's portable guidance. It must
not replace concrete requirements with vague coverage claims or treat generated starters as
proof that tests or security scans passed. See [repository import](../manual/cis_repo_import.md)
for review scope, evidence hashing, editable proposals and retirement behavior.

## Qualified Windows .NET milestone

The [readiness record](../../planning/engineering-defaults-trial-readiness.md) maps the completed IP/EQ/SC work
and native receipts; the [trial guide](../../planning/engineering-defaults-dotnet-trial-guide.md) covers onboarding
through the next task's reassessment. This qualifies a Windows .NET/C# trial, not every stack or every scanner.

Declared or detected C# initialization installs ignored local reference examples and preserves opt-out and edits.
Native harness examples include unit, architecture, integration, business, API compatibility, browser and security
checks, test-mode logs/metrics/traces, and bounded performance/mutation evidence. Actual commands and project
applicability still require verification. Two providers executed the synthetic interval CLI/portable-skill fixture; bundled reference CLI verification
remains its separate native test/export evidence. Native permission rules remain distinct from sandbox confinement. Adopted task gates require current final-state evidence
and an independent reviewer, including missing checks that a passing workflow alone could conceal.

---
title: "Agent-facing CLI and skills defaults plan"
type: implementation-plan
status: Draft
version: "0.1"
scope: "Product:ChangeImpactStudio"
owner: "Andrew Spiteri"
last_reviewed: "2026-10-09"
review_cadence: "before implementation and at each milestone"
cis:
  stable_id: change-impact-studio:plan:agent-interface-defaults
---

# Agent-facing CLI and skills defaults plan

Execution sequencing and .NET trial readiness are defined by the [consolidated implementation plan](engineering-defaults-implementation-plan.md). This document retains the CLI/skills design; its task labels are cross-references, not a duplicate execution ledger.

## Implementation disposition — 9 October 2026

The Windows .NET milestone qualifies the bundled reference CLI through native tests and export checks. Actual Codex and Claude execution of the separate synthetic interval/portable-skill fixture qualifies the tool integrations. Each application still owns its supported operations, access policy and evidence. See the [completed milestone and evidence](../../planning/engineering-defaults-trial-readiness.md) and [trial guide](../../planning/engineering-defaults-dotnet-trial-guide.md). The detailed proposal below retains design rationale and later scope; its Draft metadata is not changed into policy or product approval.

## Proposed default

CIS should recommend an agent-facing CLI and maintained skills as a default part of application design and delivery. An agent should be able to discover supported capabilities, inspect state, execute authorized workflows and diagnose failures through a documented interface. This covers application use and operations as well as development, testing and maintenance.

The CLI provides executable behavior; skills explain when and how to use it, required inputs, expected outcomes and verification. Both should be tool- and provider-neutral. Their usefulness must not depend on one model, vendor or editor, or on an agent knowing undocumented internals.

This Draft records planning requested on 5 October 2026. It retains the original default-standard and implementation proposal; it does not activate policy, generate application commands or skills, install tools or authorize operational actions. Public examples must be synthetic and independently reproducible. Closed-source application names, paths and implementation evidence remain outside this repository.

## 1. Applicability and adoption

| Project shape | Expected starting point |
| --- | --- |
| CLI application or developer tool | Make existing commands discoverable, scriptable and testable; add skills for supported workflows. |
| Service, worker or application with a UI | Provide a CLI for applicable application workflows, inspection, diagnostics and operations through normal service/API boundaries. Start with the highest-value agent tasks and record remaining coverage. |
| Library or SDK | Supply skills and supported development/test commands; add a diagnostic or sample CLI only where it serves a real consumer workflow. Do not force a deployed runtime or artificial command for every method. |
| Existing application | Preserve a suitable existing CLI and extend gaps incrementally. Where an API or other automation surface already exists, a thin CLI adapter can reuse it. Record justified alternatives or inapplicability through normal standards conformance. |
| Documentation-only repository | Provide applicable authoring/validation workflows and skills; application-runtime commands are inapplicable. |

Select the default during repository initialization/import and reassess it when source, component roles or capabilities change. Include the agent interface in feature design rather than append it after all other implementation work. For each applicable workflow, record the supported command, associated skill, verification and any reason it cannot be automated. A status command alone does not establish complete workflow coverage.

Existing authorization and stronger repository policy remain authoritative. A default standard should steer new work and expose gaps without automatically replacing adopted tooling or imposing a whole-application rewrite.

The [iteration alignment plan](iteration-alignment-plan.md) requires graph rebuild, CIS dependency reconciliation and alignment review as each task closes. CLI capability coverage and skill freshness should participate in that shared process rather than rely solely on initial onboarding.

## 2. Proposed CLI contract

These are candidate requirements for the future standard, not active rules.

| Concern | Expected behavior | Verification |
| --- | --- | --- |
| Discovery | Useful help, version and capability information; examples identify prerequisites and command availability. | A reader or agent can discover and invoke a representative workflow without source-code knowledge. |
| Noninteractive execution | Explicit arguments or structured input support unattended invocation. Missing input returns an actionable error rather than waiting indefinitely for a prompt. | Run without an interactive terminal; verify invalid/missing input and authentication-unavailable outcomes. |
| Stable results | Human-readable results and a stable structured format, normally JSON; documented exit codes and structured errors. Keep diagnostics/progress separate from machine-result stdout. | Parse success and failure results; ensure logs cannot corrupt them and exit status agrees with the outcome. |
| Bounded output | Filters, limits and pagination for large results; indicate omitted or partial data and how to retrieve more. A compact agent projection is optional when structured output is already adequate. | Exercise large inventories, partial results and paging; silent truncation cannot look complete. |
| Shared application behavior | Commands call existing use cases or APIs with normal validation, authorization and business invariants. Keep command parsing/rendering separate from business logic. | Contract/integration tests show CLI and other supported interfaces use consistent application behavior. |
| Explicit effects | Distinguish observation from mutation; identify the target environment and operation scope. Provide preview/dry-run where it meaningfully predicts a consequential change and disclose its limits. | Verify read-only commands have no business side effects and previews do not apply mutations. Existing authorization governs execution; do not require redundant approval for every command. |
| Reliable automation | Define timeouts, cancellation, retry semantics and idempotency where applicable. Long-running work exposes status/correlation and resumability where supported. | Test interrupted operations, retries, duplicate requests and partial failure without assuming every operation is safely retryable. |
| Diagnosis | Actionable failures, correlated logs/metrics/traces and bounded evidence references. Protect secrets and sensitive values in arguments, results and logs. | Fault tests connect command failure to useful diagnostic evidence; test-mode instrumentation follows the testing plan. |
| Compatibility | Version the interface contract and document deprecation/breaking changes to commands, flags, schemas and exit codes. | Compatibility fixtures cover representative automation clients and skill examples. |

Use established language-appropriate CLI facilities and the project's existing packaging/distribution. Do not build a custom parsing or automation framework merely to satisfy this standard. Installation and invocation must be documented and reproducible; do not rely on an author's checkout layout or machine-local scripts.

Agent interaction must not become a privileged back door: use normal application authentication and permissions, and do not expose unrestricted SQL, arbitrary code execution or hidden administrative operations as convenience features. Additional protocols or tool integrations can wrap the same use cases later; none is a mandatory vendor dependency.

## 3. Proposed skill contract

Provide discoverable repository-owned skills for the supported workflows. A skill should state its purpose and trigger, required context and prerequisites, exact supported invocation or a link to canonical command help, relevant permissions/effects, expected result, verification, failure recovery and limitations. Keep content concise and load detailed references only when needed.

Start with capability discovery, setup/validation, one representative application workflow, and troubleshooting where applicable. Add development, testing, linting and release/operations skills according to actual project scope. Organize skills around useful tasks rather than create one file for every flag or duplicate the entire command reference.

Skills should route agents through supported interfaces. They should not teach agents to edit internal state, bypass application rules or reconstruct business logic in ad hoc scripts. A skill does not grant authorization to execute an operation. Explicit user authority and the application's own controls still apply.

Maintain one canonical workflow description with portable skill content. Agent-specific installation paths or wrappers may adapt that content where required, but must not fork policy or hardcode a particular provider. Declare supported tool integrations and limitations rather than assume every agent understands the same packaging format.

Tie skill maintenance to command changes: update examples and prerequisites alongside the interface, validate referenced commands and documents, and report stale or contradictory guidance. Preserve human-owned skills during initialization and upgrades. Extend CIS's existing skill inventory, validation and audit mechanisms rather than introduce a second skill registry.

## 4. Existing CIS foundations and planned integration

CIS's [agent guidance](../../AGENTS.md) already requires structured output, parseable stdout, stable exit codes and explicit command ownership for CIS itself. Its [output-design article](../articles/56-designing-human-json-and-agent-output.md) explains human, JSON and compact agent projections. These are useful design inputs, not proof that governed applications have adopted equivalent contracts.

The [skills module](../../src/Cis.Modules.Skills/SkillsModule.cs) already exposes inventory, validation, import and audit, while the [default standard registry](../../src/Cis.Modules.Repository/DefaultStandardRegistry.cs) and [implementation skill registry](../../src/Cis.Modules.Repository/ImplementationSkillPackRegistry.cs) provide starter-selection integration points. Inspect exact callers and adoption behavior before implementation. This plan does not claim a complete impact analysis or a working application-interface generator.

The [code quality and architecture plan](code-quality-architecture-plan.md) should keep CLI adapters thin and skills aligned with readable ownership boundaries. The [testing harness plan](testing-harness-defaults-plan.md) should supply command contract, workflow, regression, fault and instrumentation tests. Secondary-agent review remains provider-neutral and should assess logic and operational consequences of commands as appropriate.

## 5. Qualification and evidence

Create a small public synthetic application with a CLI and skills for one real workflow. From a clean setup, a reviewer should be able to discover capabilities, invoke the workflow noninteractively, parse the result, verify state and diagnose an intentionally induced failure by following the skill. Include mutation only within an isolated test environment.

Use native automated tests for output schemas, exit codes, business/API parity, authorization, cancellation, retries, bounded output and compatibility as applicable. Include broken examples: nonexistent command in a skill, stale argument name, malformed JSON, a required prompt in unattended mode, missing diagnostics and a command that bypasses a declared application boundary. Report missing applicability/evidence separately from successful checks.

Qualify portability with at least two supported agent-tool integrations using the same canonical workflows. Keep deterministic CLI/skill checks separate from model-assisted usability evaluation: record tasks completed, context needed, failures and review limits rather than promise every agent will succeed. Automated tests do not replace UI testing for the user-facing UI.

## 6. Bounded implementation sequence

| Task | Depends on | Proposed deliverable and acceptance |
| --- | --- | --- |
| AI-01: default standard and applicability | — | Define portable CLI/skill requirements, component applicability, alternatives and conformance mappings; distinguish declared coverage from demonstrated capability. |
| AI-02: discovery and starter steering | AI-01; shared classification work in the testing plan | Detect existing interfaces/skills, recommend missing capabilities during init/import/growth, and preserve adopted tools and human edits. Demonstrate idempotent reruns and explicit coverage gaps. |
| AI-03: synthetic example and skills | AI-01; applicable testing and quality profiles | Deliver a small native CLI over shared application behavior with public skills and diagnostics. Verify clean installation, unattended execution and application behavior. |
| AI-04: verification and drift checks | AI-02, AI-03 | Connect native CLI tests and existing skill validation to delivery evidence; detect stale guidance, incompatible results and missing required workflows. |
| AI-05: existing-project adoption and portability | AI-03, AI-04 | Demonstrate incremental adoption, an appropriate library/docs-only exception and two agent-tool integrations without duplicated workflow policy. Keep private migration evidence with its owner. |

At the original drafting checkpoint, all tasks were proposed: drafting changed no source, skills or runtime configuration, and only documentation validation and source inspection had run. The implementation disposition above and the [consolidated implementation plan](engineering-defaults-implementation-plan.md) now record the completed Windows .NET milestone and its qualification. Adoption remains bounded by applicable workflows; it does not require exposing every internal operation.

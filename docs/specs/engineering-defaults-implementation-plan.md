---
title: "Engineering defaults implementation plan — .NET trial readiness"
type: implementation-plan
status: Draft
version: "0.1"
scope: "Product:ChangeImpactStudio"
owner: "Andrew Spiteri"
last_reviewed: "2026-10-09"
review_cadence: "at each implementation milestone"
cis:
  stable_id: change-impact-studio:plan:engineering-defaults-implementation
---

# Engineering defaults implementation plan — .NET trial readiness

## Outcome and authority

Deliver a qualified CIS build that can initialize a fresh .NET/C# project, guide it towards the agreed engineering defaults, keep those defaults aligned as it grows, and prevent incomplete tasks from being recorded as complete. The user selected .NET/C# for the new-project trial on 5 October 2026. The release-readiness result must say whether that trial can begin and identify the exact CIS artifact and supported environment.

This is the consolidated execution plan for the proposals below. Its Windows .NET milestone is complete as recorded in section 6 and the [final readiness record](../../planning/engineering-defaults-trial-readiness.md); task lifecycle and approvals remain in their original governed records. Creating this plan does not activate standards, approve a governed delivery record, install packages, invoke external reviewers, publish a release or create the user's new project.

| Design reference | Role in this plan |
| --- | --- |
| [Testing harness defaults](testing-harness-defaults-plan.md) | Native .NET harness, applicable assurance layers, business acceptance, instrumentation and performance evidence. |
| [Code quality and architecture](code-quality-architecture-plan.md) | Readability, cohesion, explicit dependencies, adaptable architecture, linting and independent provider-neutral review. |
| [Agent-facing CLI and skills](agent-interface-defaults-plan.md) | Supported application workflows, machine-readable commands and maintained portable skills. |
| [Iteration alignment and completion](iteration-alignment-plan.md) | Graph/dependency refresh, all-standard reconciliation, complete gate inventory, source-backed review and final-state evidence. |
| [Improvement inventory](../references/cis-assurance-improvement-candidates-2026-10-05.md) | SC-01 through SC-17 traceability; proposals are not closure evidence. |

This document owns combined sequencing and .NET trial-readiness criteria. The four design plans retain their detailed contracts and rationale; their TH/QA/AI/IA task labels are cross-references, not additional duplicate work queues. Record implementation progress and execution evidence in the governed task/verification records created for this delivery.

## 1. Release boundary

**Required for the .NET trial:** classification and growth detection; non-destructive reconciliation; qualified .NET recipes; code-quality/architecture guidance and linting; every applicable test layer; logs/metrics/traces and performance comparison; representative business acceptance; CLI/skills defaults; interchangeable secondary reviewers; context freshness/sufficiency; iteration instructions; all-gates-pass task completion; compatibility and clean-install qualification.

The reference suite must cover both a CLI-only project and an API application with a small browser-facing fixture. Browser inapplicability for a CLI must be recorded, while browser capability is demonstrated separately. Use public synthetic fixtures, including a legacy-harness fixture for migration checks; no private project is a release prerequisite or a source of published evidence.

Qualification must also begin from an unimported, multi-phase synthetic BRD. Preserve business requirements, acceptance IDs, cross-references, selected decisions, unresolved decisions and phase dependencies through the existing intake/planning path. Statements inside an input document are source evidence, not executable instructions or independently verified approval authority. This is a regression/qualification requirement for existing intake capabilities, not a new BRD authoring product.

For the CLI/data-processing rehearsal, include real PostgreSQL persistence, deterministic replay, duplicate/out-of-order events, precise value conservation and stop/restart behavior using an independently authored neutral workflow. Keep simulated, external-test and operational modes distinct. Do not require live external accounts or real operational actions to certify CIS's own default tooling.

**Subsequent milestones:** fully qualified TypeScript/JavaScript and Python recipes, broader platform/language coverage, optional shared-package extraction and migration of existing applications. Preserve those proposals; report them as pending. Existing non-.NET choices must survive reconciliation even though their new recipes are not qualified by this milestone. A .NET-ready release is not a claim that all language recipes are finished.

Do not require a particular cloud CI service, agent vendor, DI container or architecture layout. Use the selected Windows .NET trial environment as the mandatory qualification environment; declare other operating systems supported only with corresponding evidence. Qualification records exact SDK, framework, runner, collector, browser and analyzer versions rather than floating assumptions.

## 2. Delivery rules

1. Start with a fresh source/graph impact assessment and the current governance authority. Create or derive the bounded task records and test catalogue through the existing CIS workflow, preserving approval authority rather than fabricating it. This plan has not established that the repository's current technical-intent/feature records authorize every implementation change.
2. Implement through existing repository, standards, testing, diagnostics, skills, agent, planning and verification owners. No new test framework, lint framework, review orchestrator or parallel completion ledger.
3. Keep application behavior in application boundaries, with native frameworks, explicit composition and small cohesive components. Do not implement this plan as another oversized service or collection of partial files; the quality requirements apply to CIS itself.
4. Preserve existing APIs/profiles and human-owned configuration where supported. Introduce versioned contracts and migration diagnostics; no silent replacement of selected tools, standards or commands.
5. Use focused tests for each bounded change, a deliberate failing fixture for enforcement behavior, and current evidence before task closure. Where the new completion machinery is not yet delivered, manually perform the same closing checks and record the limitation instead of claiming automated enforcement.
6. Keep tests deterministic where possible. Separate model-assisted usefulness evaluation and noisy performance observations from deterministic workflow/contract tests. Retain the first failure and explain retries.
7. Reuse the assigned reviewer. Review source, callers and evidence; findings do not authorize scope expansion or risk acceptance. External provider execution uses existing authorization and data policy.

Each task below inherits these rules and must carry a specific scope, exclusions, validation commands, evidence links and finding dispositions in its execution record. Complexity is estimated as low (L) or medium (M); if source inspection reveals a high-complexity change, split that task into bounded children before implementing it. Milestones are grouping labels, not large implementation tasks.

## 3. Dependency-ordered work

### Milestone A — Baseline and shared contracts

| Task | Size | Depends on | Bounded change | Acceptance evidence |
| --- | --- | --- | --- | --- |
| IP-00: establish delivery baseline | L | — | Inspect current graph/source, existing fixes, governing authority, packaging and targeted test status. Create the governed feature/task/test records using the current supported lifecycle. Record exact implementation owners. | Baseline source identity, existing failures, source-backed impact map and valid task/test records. No inferred approval or blanket claim that previous remediation closed these gaps. |
| IP-01: finalize portable contracts | M | IP-00 | Define versioned profile/catalogue additions, gate states, adoption precedence, evidence identity and proposed rule/conformance mappings across the four design plans. | Reviewed positive/negative schema fixtures; six gate states remain distinct; optional recipes do not become mandatory frameworks. Compatibility and authorized-update semantics are explicit. |
| IP-01a: raw BRD and phased-scope qualification | M | IP-01 | Exercise existing intake and roadmap/task derivation with a synthetic multi-phase BRD. Preserve semantic content, IDs, links, decisions and later-phase scope; repair only defects that prevent this trial path. | No dropped/rewritten requirement or invented decision/approval. Current-task completion is separate from later-phase implementation and operational activation. Material intake redesign becomes a separately scoped dependency, not hidden work. |

### Milestone B — Discovery, adoption and required checks

| Task | Size | Depends on | Bounded change | Acceptance evidence |
| --- | --- | --- | --- | --- |
| IP-02: detect source growth and actual tools | M | IP-01 | Share init/import/reassessment discovery for components, roles, capabilities, native test tools, CLI commands and skill bindings. Inspect central/inherited .NET configuration. | Empty-to-C# growth, mixed solution, executable native tests, existing alternatives and genuine custom-runner fixtures classified correctly; unknown remains unknown. |
| IP-03: reconcile managed guidance and profiles | M | IP-02 | Connect testing, quality, architecture, security, observability and CLI/skills defaults to one ownership-aware update path. Preserve edited artifacts and adopted versions. | Idempotent rerun, preserved customizations/stronger rules, explicit conflicts, authorized routine updates and no cross-product mutation. |
| IP-04: derive the required gate inventory | M | IP-01, IP-02 | Assess requirements, task impact and adopted standards independently of already configured commands. Extend existing readiness/doctor/conformance projections. | A missing required layer is reported despite all configured checks passing. Read-only assessment installs nothing; docs-only and CLI-only inapplicability is evidenced. |

### Milestone C — Qualify the native .NET harness and quality recipes

| Task | Size | Depends on | Bounded change | Acceptance evidence |
| --- | --- | --- | --- | --- |
| IP-05: native unit harness and coverage/mutation | M | IP-01, IP-03 | Qualify the selected .NET runner/platform/collector, reusable fixtures, result adapters and bounded mutation commands. Preserve meaningful test identities. | Clean native discovery/execution, actual coverage and mutation reports, useful assertions, controlled mutant detection and explicit zero-test/missing-report failures. No manual testcase/report framework. |
| IP-06: test instrumentation lifecycle | M | IP-01, IP-05 | Qualify test-mode logging, metrics, traces, correlation, bounded capture/redaction and cleanup through existing diagnostics contracts. | Known signals captured and attributable; parallel isolation, timeout/crash partial evidence and unavailable collector behavior; no changed business/auth behavior. |
| IP-07: performance evidence and budgets | M | IP-06 | Qualify predeclared workload/resource identity, comparable baselines, overhead reporting and regression assessment. | Controlled degradation detected; missing, incompatible and inconclusive evidence never passes; no automatic baseline acceptance. |
| IP-08: code-quality and architecture enforcement | M | IP-03, IP-05 | Qualify .NET analyzers/Sonar lint configuration, formatting checks, selected architecture tests and manual readability criteria. Aggregate partial-type signals and preserve narrow exclusions. | Deliberate lint/boundary violations detected, local/CI equivalence, zero-match handling and preserved alternatives. Readable responsibility/replacement example plus an over-fragmentation counterexample. |
| IP-09: integration, persistence and API contracts | M | IP-05, IP-06 | Qualify real-dependency fixtures, application hosts, migration/fault checks and API compatibility evidence using existing owners. | Rollback/concurrency or retry failure case, cleanup after failure, breaking API change detected and missing prerequisites reported honestly. |
| IP-10: representative business acceptance | M | IP-07, IP-09 | Qualify business scenarios with thin bindings for complete workflow outcomes, declared workload/resource budgets, reproducible event processing and applicable interruption/recovery or installed-artifact behavior. | Full result/readback, precision/conservation and replay without duplicate effects verified; undersized workload, timeout, incomplete output and incompatible artifact evidence cannot satisfy the scenario. |
| IP-11: browser and UI assurance recipe | M | IP-05, IP-06, IP-09 | Qualify native browser automation and applicable accessibility/component checks using a small public fixture or an existing suitable example. | Persisted user journey, denied/error case, failure trace/screenshots; CLI-only fixture remains explicitly inapplicable. No unrelated CIS editor/UI redesign. |
| IP-12: security-layer composition | M | IP-03, IP-09 | Bind applicable security behavior tests and native scanner evidence to the same harness/readiness model. Use the existing security module and planned scanner categories. | Negative access/disclosure behavior and applicable static/dependency/secret/configuration/dynamic checks; missing scanner, unsupported layer or missing authorization cannot produce a pass. Split scanner adapters if needed. |

Pin and document exact recipes during these tasks. The design plan's numerical starting points remain visible: 95% changed-production line coverage and the proposed mutation thresholds, subject to stronger adopted policy or a valid bounded exception. Tool compatibility and applicability must be qualified; do not install every tool into every project merely because it is listed.

### Milestone D — Agent interfaces, independent review and context

| Task | Size | Depends on | Bounded change | Acceptance evidence |
| --- | --- | --- | --- | --- |
| IP-13: application CLI reference | M | IP-03, IP-06, IP-09 | Provide a thin native CLI over the synthetic application's use cases, with discovery, structured results, explicit effects and diagnostic references. | Unattended success/failure, stable exit/schema contracts, bounded output, cancellation and API/business parity; no privileged bypass or custom parser framework. |
| IP-14: portable workflow skills | M | IP-13 | Add and bind canonical skills for discovery, representative use, verification and diagnosis; reuse existing skill validation/audit and command-drift checks. | Clean setup and commands reproducible from guidance; stale invocation fails validation; two supported agent-tool integrations use the same workflow content. |
| IP-15: configurable independent logic review | M | IP-01, IP-08 | Extend existing implementation/review stages with the quality review profile, common findings contract, source identity and closure rules. | Two qualified interchangeable provider adapters, fresh reviewer runs, structured findings and bounded correction rounds. Deterministic unavailable/stale/malformed-output tests; separately recorded seeded-defect evaluations. |
| IP-16: source-backed context sufficiency | M | IP-02, IP-04, IP-15 | Bind reviewer context to final source, relevant callers/contracts, execution evidence and explicit omissions. Check reused context before the next task. | Fresh-but-incomplete graph, inaccurate summary, omitted caller and intervening-edit cases expand to source or block affected review. No claim of whole-repository completeness. |

### Milestone E — Enforce the iteration completion contract

| Task | Size | Depends on | Bounded change | Acceptance evidence |
| --- | --- | --- | --- | --- |
| IP-17: iteration closing workflow | M | IP-03, IP-04, IP-14, IP-16 | Compose graph build, CIS dependency reconciliation, all-standard alignment, stabilization/rechecks and assigned-reviewer double-check. Update iteration and generated delivery instructions consistently. | Evidence for final state; verified unchanged path; canonical updates cause refresh; conflicts, concurrent edits and bounded retry exhaustion remain visible. No blind package upgrade/import. |
| IP-18: completion transition enforcement | M | IP-05 through IP-12, IP-15, IP-17 | Connect required gate inventory and alignment/reviewer receipts to existing task transitions, test traceability and final sweep. | Requirements → tasks → source → tests/results → review dispositions are current. Every applicable gate passes; failed/missing/stale/skipped block; justified inapplicability is separate; unresolved blocking findings prevent completion. |
| IP-19: compatibility and reconciliation regression | M | IP-18 | Exercise old profiles, existing tools, edited guidance, partial adoption, multi-repository ownership and local/CI invocation consistency. | Existing supported projects retain their choices; backward-compatible reads; new enforcement follows adopted policy; omission of new required gates cannot be hidden by old green results. |
| IP-20: synthetic migration/refactoring pilot | M | IP-10, IP-13, IP-18, IP-19 | Migrate a public legacy-harness fixture and extract one cohesive responsibility using the native harness and review loop. | Meaningful assertion/contract parity, traceable old/new cases, verified diagnostics and no behavior regression. Improved navigability is reviewed; file/class counts alone do not establish success. |

### Milestone F — Ready for the user's fresh .NET project

| Task | Size | Depends on | Bounded change | Acceptance evidence |
| --- | --- | --- | --- | --- |
| IP-21: empty-project rehearsal | M | IP-01a, IP-19, IP-20 | Run the end-to-end qualification scenarios below from clean disposable repositories with the release candidate. Begin with raw synthetic BRD intake, add source after initialization and complete multiple task iterations. | All applicable acceptance rows pass with exact commands, artifacts and identities. Injected faults prevent completion; corrections restore readiness without weakening policy. |
| IP-22: packaged-build and documentation qualification | M | IP-21 | Build/install the intended distributable in a clean test location; rerun a bounded bootstrap/growth/closure smoke test. Publish local release notes, supported versions, known limits and trial instructions. | The packaged binary, bundled starters and skills match tested source. No dependency on the development checkout, private data or undocumented local scripts. Existing relevant CIS regression checks and strict docs/standards/skills validation pass. |
| IP-23: independent readiness review and handoff | L | IP-22 | Assigned reviewer audits the release evidence and the readiness checklist; resolve required findings and record the final artifact identity. | Explicit ready/not-ready verdict for a new .NET/C# project, final command guide and unresolved nonblocking limits. No publication/deployment or user-project creation is implied. |

Tasks can be developed in separate bounded changes where dependencies permit. This table specifies dependency order, not authorization for concurrent agents or edits. IP-18 combines capabilities only after their focused qualification; do not defer all integration testing until the final rehearsal.

## 4. Ownership and implementation starting points

| Concern | Existing source ownership to inspect before edits |
| --- | --- |
| Classification and starter adoption | `src/Cis.Modules.Repository/RepositoryClassifier.cs`, `RepositoryTestingStarter.cs`, `RepositorySecurityStarter.cs`, `RepositoryStarterBinder*`, `DefaultStandardRegistry.cs`, `ImplementationSkillPackRegistry.cs`, importer and profile doctor checks. |
| Rule applicability and structural evidence | `src/Cis.Modules.Standards/`, existing graph/compiler providers and conformance mappings. Reuse known-pattern capability limits. |
| Harness and diagnostics | `src/Cis.Abstractions/CisTestingContracts.cs`, `src/Cis.Modules.Testing/TestingService.cs`, `TestResultAdapters.cs`, existing diagnostics/security/API/workflow modules. |
| Skills and examples | `src/Cis.Modules.Skills/`, existing generation/templates and public example ownership. Keep runtime code out of skill prose. |
| Review and source context | `src/Cis.Modules.Agent/AgentService.StoryTasks.cs`, `AgentService.StoryReviewContext.cs`, existing provider contracts/adapters, graph/index/context owners. |
| Completion and business workload evidence | Existing Plan/Verify services and task-transition paths; `src/Cis.Modules.Plan/WorkloadEvidenceReview.cs`; canonical task/test/verification records. |

These are starting points from targeted inspection, not a complete compiler-verified change graph. IP-00 must resolve callers, tests, schema impact and current authority before implementation. Earlier fixes in the [remediation record](../../planning/cis-trial-remediation-2026-10-05.md) should be reused and regression-tested, not reimplemented or treated as full acceptance evidence.

## 5. End-to-end qualification matrix

Use stable qualification labels below in the implementation test catalogue; bind them to generated test identities through the normal CIS traceability workflow. The scenarios specify outcomes, not invented future CLI command names.

| ID | Scenario | Required result |
| --- | --- | --- |
| EQ-01 | Initialize an empty repository with no source, then add a .NET component. | Growth is detected; applicable standards, skills and harness recommendations appear through reconciliation. The old empty profile cannot leave the task apparently compliant. |
| EQ-02 | Initialize with an explicitly selected .NET stack and adopt its defaults. | Compatible native harness and profiles are reproducible from versioned instructions/templates. Framework selection, readiness and execution are distinct. |
| EQ-03 | Import an existing native alternative with custom guidance. | Preserve its framework, rules and commands; expose only actual capability gaps. Rerun is idempotent. |
| EQ-04 | Execute the applicable .NET layers in CLI and API/browser fixtures. | Native unit, mutation, architecture, integration, compatibility, regression, business, browser and security evidence is real and scoped; no fabricated runner or report. |
| EQ-05 | Seed dense/overloaded code, a partial-type concentration, a lint defect and a forbidden dependency. | Correct checks/review identify concrete issues. Formatting or file splitting alone cannot resolve the responsibility finding; justified alternatives remain supported. |
| EQ-06 | Emit known test signals, then interrupt a workload. | Correlated logs/metrics/traces and available partial evidence survive; secrets are redacted and failed/incomplete capture remains explicit. |
| EQ-07 | Run a representative business workflow and controlled degraded variant. | Validate the complete result under declared workload/resource/artifact conditions. Detect the degradation; undersized, incompatible or incomplete evidence cannot satisfy acceptance. |
| EQ-08 | Follow the application skill through a compatible agent tool. | Discover and execute the supported CLI workflow, parse results and diagnose failure without reading private internals. Repeat through a second supported integration. |
| EQ-09 | Use two interchangeable reviewer adapters and seed a logic defect. | Provider-neutral selection/provenance works; actual reviewer outcomes are recorded, including misses. Deterministic stale/malformed/unavailable cases cannot masquerade as a successful review. |
| EQ-10 | Give the reviewer a fresh graph with a missing caller or misleading summary; edit inputs before the next task. | Source fallback expands material context; omissions or stale reuse prevent unqualified review/readiness. |
| EQ-11 | Finish a task whose configured checks pass while a required gate is absent. | Completion is rejected. Separately verify failed, stale, skipped, missing, justified inapplicable and passed states. |
| EQ-12 | Correct review findings, update a profile, and repeat the closing sequence. | Rebuild affected graph/evidence, re-review changed scope, preserve customizations and accept only the final consistent state. Concurrent edits invalidate affected evidence. |
| EQ-13 | Break requirement/test mapping or leave a completion-blocking finding unresolved. | Current traceability and finding closure are enforced before completion; a deferral is not reported as completed work. |
| EQ-14 | Run an unchanged or docs-only task. | Lightweight evidenced unchanged/inapplicable paths work without installing irrelevant application tools or claiming unrun checks passed. |
| EQ-15 | Use the packaged CIS build in a clean disposable workspace. | Bootstrap, source growth, native verification, review and completion work using only declared prerequisites and the packaged guidance. |
| EQ-16 | Import a raw synthetic BRD with multiple phases, stable IDs, accepted-decision claims and unresolved settings. | Preserve requirements, acceptance criteria and cross-references; distinguish source claims from confirmed authority, and preserve unanswered decisions without inventing values. |
| EQ-17 | Complete an early-phase task while later requirements and activation decisions remain pending. | All gates for the current authorized task scope pass; later-phase work stays visible with dependencies. No claim that the whole product is finished or operationally activated; no demand to implement unrelated future phases to complete the current task. |
| EQ-18 | Replay and restart a neutral persistent event-processing workflow. | Preserve precise values, event-time conventions and output identity; duplicate/out-of-order events and interruption do not create duplicate effects or fabricated outcomes. Test and operational modes remain distinguishable. |

Qualification must include intentional failures, restored passing cases and evidence that the gate actually inspected the intended scope. A mock provider is useful for deterministic contract tests but does not replace actual authorized adapter qualification. Model usefulness evaluations and performance results must state environment, method and limitations.

## 6. Finish line: .NET trial-readiness checklist

Completed for Windows .NET/C# candidate `0.3.0-engineering.20261009.34` on 9 October 2026.
The [final readiness record](../../planning/engineering-defaults-trial-readiness.md) maps every IP/EQ/SC item to its scoped evidence and limitations; the [trial guide](../../planning/engineering-defaults-dotnet-trial-guide.md) provides the next-project workflow. Historical receipts retain their original candidate identities.


- [x] All IP-00 through IP-23 tasks, including IP-01a, have current required evidence and resolved completion-blocking findings; all applicable gates pass.
- [x] EQ-01 through EQ-18 have valid outcomes, including demonstrated negative cases; justified inapplicability is explicit and does not remove the capability the release promises to support.
- [x] Every SC-01 through SC-17 has a mapped implemented prevention/detection mechanism and qualification evidence, or remains explicitly open and makes any affected readiness claim not-ready.
- [x] Native framework/tool versions and the supported Windows environment are recorded; restore/install/browser/database/scanner prerequisites are reproducible.
- [x] The tested CIS package and bundled guidance are identified by version, source revision and artifact digest; install/version/smoke instructions use verified current syntax.
- [x] A fresh .NET project can obtain defaults both with a declared stack and by growing from an empty repository.
- [x] CLI-only applicability works, and API/browser capability is separately demonstrated.
- [x] Test instrumentation and business workload acceptance are verified; smaller passing checks cannot conceal missing representative evidence.
- [x] The closing iteration sequence and reviewer double-check are enforced against final-state evidence, including missing required gates and stale source/context.
- [x] Required provider/tool qualifications are complete using permitted synthetic content; unavailable mandatory capabilities remain blockers.
- [x] The trial guide tells the user how to initialize, import a raw BRD with provenance, derive the complete phased roadmap, select the first authorized scope, execute checks, inspect a review, complete an iteration and verify reassessment on the next task.
- [x] Intake preserves all lifecycle requirements and decision boundaries; current-task completion does not invent future decisions or confer operational activation authority.
- [x] The final handoff says **Ready for a new .NET/C# project trial** or **Not ready**, with evidence and precise remaining limitations. It does not claim TypeScript/Python qualification or approval of the user's future application.

Do not make the user's real project the first integration test for these features. Complete the synthetic rehearsal and packaged-build check first. The user's project then tests practical usability and emerging project needs rather than basic wiring that should already have been qualified.

## 7. Traceability to the component plans

| Consolidated work | Earlier planning labels | Improvement coverage |
| --- | --- | --- |
| IP-00 through IP-04 | TH-01/02/03/04; QA-01/02; AI-01/02; IA-01/02 | SC-03/09/12/14 and shared defaults/adoption. |
| IP-05 through IP-12 | TH-05 and TH-12a/b/c; QA-03/03a/04 | SC-01 through SC-11 and SC-17, with native harness, lint/architecture and business acceptance. |
| IP-13 through IP-16 | AI-03/04/05; QA-05/05a; IA context requirements | Agent-facing CLI/skills and secondary review; SC-01/04/15/16. |
| IP-17 through IP-20 | TH-08/09/10; QA-06; IA-03/04/05 | SC-09 through SC-16; actual completion enforcement and bounded synthetic migration. |
| IP-21 through IP-23 | .NET portion of TH-11 plus combined release qualification | Evidence-backed .NET trial readiness across all seventeen candidates. |
| Subsequent work | TH-06/07 and remaining TH-11; QA-07/08 | Additional language qualification and optional reusable-package assessment; not represented as delivered by this release. |

Some mechanisms address multiple candidates; the closure report must link each claim to its actual evidence rather than infer closure from this mapping table. Existing private-application failures are not resolved merely because CIS gains a better gate.

## 8. Decisions to settle during implementation

IP-01 records final schema fields, rule severities, adoption/update semantics and receipt identity. IP-05 through IP-12 select and qualify exact tool versions, diagnostic limits, scanner applicability and representative performance budgets. IP-15 qualifies available reviewer integrations without hardcoding providers. IP-22 resolves the distributable/version and verifies all documented commands against that build. These are bounded implementation decisions, not reasons to invent unsupported commands in this plan.

No elapsed-time estimate is asserted before IP-00 sizes source and compatibility work. Completion is determined by the checklist and evidence. The initial planning pass used source/document review only. Subsequent implementation, native qualification, provider execution and readiness review are recorded separately in the final readiness record; the original scope and criteria below remain traceable.

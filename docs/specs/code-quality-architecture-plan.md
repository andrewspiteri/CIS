---
title: "Code quality and architecture quality plan"
type: implementation-plan
status: Draft
version: "0.1"
scope: "Product:ChangeImpactStudio"
owner: "Andrew Spiteri"
last_reviewed: "2026-10-05"
review_cadence: "before implementation and at each milestone"
cis:
  stable_id: change-impact-studio:plan:code-quality-architecture
---

# Code quality and architecture quality plan

## Recommendation and scope

CIS should establish a common quality baseline, supply adaptable architecture recipes, and recommend focused shared libraries where their benefit is demonstrated. A contributor should be able to locate a responsibility, follow its behavior, identify its dependencies and failure paths, and replace an external dependency without understanding the whole application.

The recommended default is readable, cohesive code with explicit dependencies and enforceable ownership boundaries. Architecture layout, DI container, persistence technology and runtime libraries remain repository choices. A small library or CLI should be able to meet the baseline without adopting a modular-monolith framework.

This Draft records planning requested on 5 October 2026. It proposes work on SC-01 through SC-04, with related prevention for SC-12, SC-14 and SC-15 in the [CIS assurance improvement candidates](../references/cis-assurance-improvement-candidates-2026-10-05.md). It does not activate standards, select packages, refactor production code or approve a delivery plan. The [testing harness plan](testing-harness-defaults-plan.md) supplies the behavioral and instrumentation baseline needed before production refactoring.

Public qualification examples must be synthetic and reproducible from CIS fixtures. Application names, source identifiers, checkout paths and operational evidence from closed-source projects remain outside this repository. A future project-specific migration requires its own record in the owning repository.

## 1. Three levels of guidance

| Level | Proposed content | Adoption behavior |
| --- | --- | --- |
| Common quality baseline | Naming, readable control flow, cohesion, explicit dependencies, side-effect ownership, failure handling, navigability and review evidence. | Offer to every implementation component. Translate checks to the language; disclose unsupported automated checks. |
| Architecture recipe | Responsibility map, composition, module contracts, allowed dependency directions, visibility, placement and examples appropriate to the application. | Preserve existing adopted architecture. Recommend a small starting recipe when none exists; record alternatives and reasons. |
| Reusable implementation | Established ecosystem libraries first; optional narrow shared packages or adapters when useful across real consumers. | Never require an umbrella runtime library merely to pass CIS governance. Adoption and upgrades remain explicit repository changes. |

Discovery must not silently bless poor existing code. Report both the observed structure and how it differs from adopted policy or the proposed baseline. Preserve valid alternatives; expose missing decisions and conflicting rules. Once a repository adopts a requirement, enforce it through its existing conformance and delivery workflow.

Reassess applicability when an empty repository gains source or a new component/language appears. Share this reconciliation with the testing plan rather than build a second classification mechanism. Upgrades should show proposed additions and conflicts without overwriting human-owned guidance.

## 2. Existing foundations and the remaining gap

PARR's C# guidance covers explicit, maintainable code, cohesive classes, constructor injection, meaningful names, lifetime ownership and module boundaries. Its [C# instructions](C:/miscwork/portfolio/PARR/.github/instructions/csharp.instructions.md) are a useful adaptation source. Its [solution implementation standard](C:/miscwork/portfolio/PARR/docs/generic/standards/csharp-solution-implementation-standard.md) also includes a four-project module structure and specific persistence choices. Those heavier choices should remain selectable recipes rather than universal CIS requirements.

CIS already has a [standard registry](../../src/Cis.Modules.Repository/DefaultStandardRegistry.cs), [implementation skill registry](../../src/Cis.Modules.Repository/ImplementationSkillPackRegistry.cs), [known-pattern provider](../../src/Cis.Modules.Standards/BuiltInStandardPatternProvider.cs) and [pattern inference specification](standard-pattern-catalogue-and-inference-spec.md). Known patterns include thin endpoints, application handlers, options binding and structured logging. They provide a foundation for guidance and structural review; observing a pattern does not prove maintainability or establish an adopted rule.

Sampled [generated C# guidance](../../.github/instructions/cis-host-csharp.instructions.md) concentrates on builds, warnings and source inspection. The improvement is to connect a coherent quality baseline to applicability, implementation steering, examples and review evidence. Passing compilation or prose-readability checks cannot establish code readability.

This is targeted source and documentation review, not a complete implementation audit. No application tests, package qualification or graph-wide impact analysis were performed for this proposal.

## 3. Proposed code-quality baseline

The identifiers below are planning labels, not active standard rules. Each eventual rule needs scope, severity, verification method and an exception route in the existing standards system.

| Label | Expected outcome | Evidence and limits |
| --- | --- | --- |
| CQ-READ | Use meaningful domain names, readable blocks and explicit control flow. Avoid compressed sequences of unrelated statements and expressions with hidden side effects. Explain non-obvious reasons and invariants in comments. | Native formatting/style checks plus source review. Idiomatic compact expressions remain valid when they improve clarity. |
| CQ-COHESION | Each class, module and function has a coherent responsibility. Orchestration coordinates named collaborators; persistence, policy and transport concerns have clear owners. | Responsibility review and complexity/coupling signals. Count the complete symbol across partial files. File splitting alone is not decomposition. |
| CQ-CONTRACT | Inputs and outputs express meaning. Prefer named fields or focused value types where long positional argument lists are ambiguous. Avoid boolean switches that combine unrelated operations. | API and call-site review. Do not hide excessive dependencies inside a generic context object merely to reduce parameter counts. |
| CQ-DEPENDENCY | Dependencies are visible and supplied at real external, side-effect or variation boundaries. Construction and configuration have identifiable owners. | Review constructors/functions and composition; verify supported replacements. Avoid ambient service lookup in business logic. Pure helpers and value objects need no container. |
| CQ-LIFECYCLE | Cancellation, concurrency, transaction ownership, resource disposal and async behavior are understandable and intentional. | Targeted analyzer, composition and behavior tests. Static structure alone cannot prove runtime lifetime correctness. |
| CQ-FAILURE | Failure handling preserves actionable context and follows explicit retry, propagation, recovery or best-effort policy. | Failure-path tests and review. Broad catches, silent fallbacks and unobserved tasks need scrutiny; logging failures may require an independent bounded fallback. |
| CQ-NAVIGATION | Folders, namespaces and names reveal responsibility and ownership. A reader can find entry points, composition, behavior, external adapters and relevant tests. | A brief module map where useful and a source walkthrough. Avoid growing generic helper areas without a precise purpose. |
| CQ-SIMPLICITY | Add an abstraction when it creates a meaningful boundary, isolates a dependency or supports actual variation. Keep straightforward behavior local. | Review both excessive concentration and excessive fragmentation. Do not require an interface per class, inheritance hierarchy, mediator, repository wrapper or DI registration for every function. |

Metrics are review signals, not a definition of good design. Record nesting, method/type complexity, dependency fan-out, parameter counts, cycles and change hotspots where reliable tools support them. Calibrate initial warning thresholds on representative code before selecting hard limits. Include legitimate complex algorithms, declarative mappings and generated code in qualification to expose false positives.

Generated-code exclusions need identifiable provenance and bounded scope. Assess generator ownership and handwritten extensions separately. Renaming, moving code, adding partial files, broad suppressions or hiding calls behind a generic facade must not erase unresolved findings.

## 4. Architecture choices and language adaptation

For a new .NET service or worker without an adopted architecture, recommend a small feature-oriented structure with an explicit composition root, named use cases, meaningful domain logic and external adapters. These are logical responsibilities: introduce separate assemblies when they protect a real ownership, deployment or dependency boundary. Do not generate four projects per feature by default.

| Component shape | Initial recipe | Growth trigger |
| --- | --- | --- |
| Small library, CLI or tool | Cohesive modules/functions, explicit inputs and outputs, adapters for external effects, simple manual composition when sufficient. | A genuine dependency or ownership boundary needs enforcement or independent replacement. |
| Service or worker | Feature/use-case ownership, thin entry points, explicit business behavior, external adapters and host composition. | Multiple independently owned capabilities or cross-feature coupling justify module contracts. |
| Modular application | Named modules, limited public contracts, declared dependency direction and an enforceable ownership map. | Extract a package or deployment only for demonstrated consumer or operational needs. |
| Existing layered, vertical-slice, functional or framework-led project | Map the same quality outcomes onto its current idioms. | Change architecture only where evidence establishes a concrete maintenance or correctness benefit. |

Deliver the C# profile first. For an unconfigured .NET application that needs a container, recommend platform DI facilities and existing logging abstractions before designing equivalents. Preserve supported existing containers and logging frameworks. Prefer constructor injection; keep service resolution at composition or explicitly owned infrastructure scope boundaries, and test lifetimes and replacement behavior. Microsoft's [DI guidance](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/guidelines) and [logging documentation](https://learn.microsoft.com/en-us/dotnet/core/extensions/logging/overview) support these starting choices; they do not establish compatibility for every application.

TypeScript/JavaScript and Python profiles should follow with idiomatic modules, functions and explicit dependency arguments or framework-native injection. They should not reproduce C# class/interface/container patterns mechanically. Until their examples and checks are qualified, describe them as planned coverage.

Every adopted architecture map should identify responsibility owners, allowed dependencies, public contracts, composition locations, external effects, applicable test rules and known exceptions. Define the actual rule before writing tests around incidental folder or namespace names. Graph inference assists discovery; it must not manufacture an architecture decision from repeated code.

## 5. Shared-library decision

| Option | Benefit | Cost | Recommendation |
| --- | --- | --- | --- |
| One required common runtime for all CIS projects | Central implementation and conventions. | Broad dependencies, coupled upgrades and a large migration burden; does not itself prevent unreadable application code. | Do not make this the CIS default. |
| Guidance and templates only | Low adoption cost and easy adaptation. | Repeated infrastructure can drift; examples alone cannot enforce quality. | Use for initial education and scaffolding, alongside checks. |
| Quality baseline, recipes and optional focused packages | Common outcomes with selective reuse and enforceable boundaries. | Requires clear ownership, qualification and compatibility support. | Recommended direction. |

Use this reuse order: existing repository capability, established ecosystem capability, a focused local implementation, then a shared package when common needs are proven. The proposed default extraction trigger is at least two concrete consumers with compatible requirements, or an explicit platform requirement with a documented benefit and cost. Mere possibility of future reuse is insufficient.

A package candidate needs a narrow purpose, named owner, small public contract, declared dependencies, version/compatibility policy, tests and migration guidance. Separate framework/engine adapters from neutral contracts where useful. Keep application business policy out of generic infrastructure. Test replacement, disposal and upgrade behavior against actual consumers before promotion.

Possible later candidates include observability configuration, test instrumentation support and repeated integration adapters. Inventory existing capabilities and licensing before selecting or publishing an extraction. A template gives the destination repository ownership of generated code; a package creates a maintained runtime dependency. Document that distinction. Good organization in a source library is not, by itself, evidence that the whole library is portable or appropriate for another project.

## 6. Reference examples

Create an independently authored, public .NET example showing a small application operation with structured logging, a replaceable external adapter and explicit composition. Use the testing plan's native harness. Explain where each responsibility lives and why it exists.

Use logging to illustrate decomposition: event creation/context, filtering or enrichment, routing/output, and composition. Add buffering and batching only in a separate example that requires asynchronous delivery. Prefer native logging extension points; the example must not become a competing logging framework. Show both a minimal solution and a justified extension so agents do not reproduce every optional component.

The example's acceptance evidence should demonstrate:

1. A reader can find where an event originates, where output is selected, and how failure is handled.
2. A writer/provider or external adapter can be replaced at composition without editing business logic; unit tests need no production service provider.
3. Integration tests exercise real composition and catch invalid scopes, missing registrations and disposal mistakes where the chosen container supports those checks.
4. Test mode activates the logs, metrics and traces specified in the testing plan; correlation and captured failure evidence remain useful after refactoring.
5. A declared forbidden dependency and an invalid composition fixture fail the relevant checks. Inspecting zero types or tests cannot produce a successful assurance result.
6. The optional buffered example specifies capacity, ordering, overflow, cancellation, flushing/shutdown and failure behavior. Performance comparison includes instrumentation overhead under comparable conditions.

Also provide a short before/after example of dense orchestration decomposed into cohesive responsibilities, with behavior-preserving tests. Include counterexamples for excessive interfaces, misleading parameter objects, many tiny forwarding classes and a large partial type spread across files. Demonstrate that line count and class count alone cannot decide quality.

## 7. Enforcement and review

| Check | Role | Proposed enforcement |
| --- | --- | --- |
| Formatting, compiler diagnostics and selected native analyzers | Repeatable syntax/style/correctness checks. | Gate new violations of adopted rules. Preserve deliberate, narrow suppressions and report their rationale. |
| Project/module dependency, visibility and cycle rules | Protect the adopted architecture's ownership boundaries. | Deterministic native architecture tests, qualified against a deliberate violation. Use the testing plan's selected framework. |
| Composition, lifecycle and replacement tests | Verify real wiring, scopes and disposal behavior. | Run focused integration checks appropriate to the host; do not equate container validation with all runtime behavior. |
| Complexity, concentration and coupling signals | Locate code that deserves closer review. | Advisory initially; repository-calibrated budgets only after qualification and adoption. Report complete symbols and scope. |
| Source-aware readability and responsibility review | Assess comprehensibility and design tradeoffs. | Record concrete findings and dispositions. Model feedback remains advisory, not automatic approval. |

For .NET, qualify SDK analyzers and repository formatting configuration before adding more tooling; [Microsoft's code analysis documentation](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/overview) describes available mechanisms. Reuse the test-harness plan's architecture tooling instead of writing another analysis or test framework. Add narrowly scoped custom analysis only for a demonstrated rule that existing tools cannot enforce.

Source-based checks should consume compiler-resolved information where available. Missing or stale graph capability produces an explicit unavailable/incomplete result, not a guessed success. Retain the [existing graph and inference limits](standard-pattern-catalogue-and-inference-spec.md): bounded static evidence does not prove branch, interprocedural or runtime semantics. CIS must not load target assemblies as plugins; native tests run through authorized repository commands.

Review a representative flow from entry point through policy and external effects, including callers and failure paths. Ask where a maintainer would change a rule, replace an adapter or diagnose a failure, and whether doing so requires unrelated code changes. A clean formatter, high coverage or green architecture test cannot answer those questions alone.

## 8. Existing repositories and bounded refactoring

Start with a recorded baseline: adopted decisions, actual source structure, concrete quality findings, applicable tests and current failures. Preserve supported architecture choices while identifying debt. Newly adopted checks can prevent new violations without requiring a full cleanup first. Existing exceptions remain visible with an owner, reason and review condition; do not automatically baseline new violations to make a build pass.

For changed code, use a bounded improvement rule: do not add a responsibility to an already overloaded type without addressing or explicitly disposing that finding. Improve the affected responsibility and its immediate seam; unrelated legacy code should not force a repository-wide rewrite. A higher-risk boundary violation may need repair before work can safely continue, according to adopted policy.

The later application pilot should proceed after a qualified native test harness covers the selected behavior:

1. Choose one bounded hotspot by maintenance pain, coupling, failure history and ability to verify it. Select actual source scope in the application's own change record.
2. Capture existing behavior and important contracts through characterization/regression tests, including errors, ordering, cancellation, numerical behavior and persistence effects where relevant. Preserve known failures rather than encode a suspected bug as intended policy.
3. Record current responsibilities and intended owners. Extract one cohesive responsibility at a time behind the smallest useful seam; keep callers compatible where possible.
4. Run focused unit, architecture, integration and contract/regression checks. Compare declared performance budgets with comparable instrumentation and workload evidence; use mutation testing for affected high-risk logic where applicable.
5. Review the resulting code for navigability, explicit dependencies, debugging paths and needless indirection. Keep application-specific evidence with its owner; public closure needs reproducible synthetic fixtures.

Do not combine this pilot with a framework rewrite, new persistence model, business behavior changes or a broad shared-library extraction. Existing workload failures remain unresolved unless directly investigated with new evidence. A refactoring success cannot retrospectively turn a failed workload into a pass.

## 9. Bounded implementation sequence

All tasks are proposed and not started. Task names are planning labels; formal delivery records remain subsequent work.

| Task | Depends on | Bounded deliverable | Acceptance evidence |
| --- | --- | --- | --- |
| QA-01: portable quality contract | — | Map PARR's reusable C# guidance to common outcomes; define the initial C# profile, architecture-map content, policy precedence and review signals. | Examples distinguish policy from convention, small applications from modular systems, and generated from handwritten code. No universal container or layout requirement. |
| QA-02: discovery and propagation | QA-01; testing TH-02/TH-03 classification work | Wire quality guidance into initialization, import, source-growth reconciliation and relevant implementation skills/instructions. | Empty-to-source, existing architecture, edited guidance and multi-component fixtures; repeat runs preserve choices and expose conflicts. |
| QA-03: deterministic checks | QA-01; testing TH-01/TH-04 contracts | Map adopted quality and architecture rules onto native analyzers/tests and existing conformance/evidence handling. | Positive and negative fixtures, partial-type aggregation, zero-match detection, stale analysis, missing tooling and bounded exceptions. No synthetic quality pass. |
| QA-04: reference example | QA-01, QA-03; testing TH-05 and TH-12b | Deliver minimal and extended .NET examples with composition, replacement, native tests and test instrumentation. | Clean restore/build, expected failures detected, useful failure evidence and documented ownership. Split buffering/performance qualification into a follow-up slice. |
| QA-05: implementation and review steering | QA-02, QA-03, QA-04 | Require an appropriate responsibility map before complex implementation and evidence-based quality review before completion; reuse existing planning/review workflow. | Fixtures where an overloaded type, partial-file split or broad suppression cannot be reported as resolved merely because checks pass. Simple changes retain lightweight handling. |
| QA-06: bounded refactoring pilot | QA-05; applicable testing TH-10 migration and baseline evidence | Refactor one selected responsibility under a separate application implementation scope. | Behavior/contract parity, boundaries, diagnostics, comparable performance and a source review; precise before/after findings rather than a class-count claim. |
| QA-07: broader language recipes | QA-01 through QA-05; relevant testing TH-06/TH-07 | Qualify TypeScript/JavaScript and Python recipes using their own idioms and adopted tools. | Equivalent outcomes demonstrated without forced C# structure; unsupported analysis remains explicit. |
| QA-08: optional shared capability assessment | QA-04; consumer evidence | Inventory repeated needs and evaluate ecosystem reuse versus narrow extraction. | A justified package proposal or documented decision to keep code local. Package delivery, if chosen, gets its own plan. |

Likely CIS implementation surfaces are the standard and implementation-skill registries, repository starter binding, standards patterns and conformance, source graph capabilities, and existing planning/context/review/verification integration. Exact callers and schema changes need fresh graph/source inspection before implementation; this is not a complete impact analysis.

Deliver QA-01 as the first bounded code-quality change after this proposal is reviewed. Testing improvements can continue first as requested; production refactoring waits for the relevant behavioral baseline. Optional package work is not a prerequisite for guidance, enforcement or the pilot.

## Proposed decisions and review limits

The recommended decisions are: common quality outcomes; adaptable architecture recipes; C# first; ecosystem reuse before custom infrastructure; optional focused shared packages; explicit dependencies without mandatory containers; aggregate partial types; deterministic enforcement for adopted structural rules; advisory complexity analysis plus source review; gradual improvement in existing repositories; test and instrumentation evidence before production refactoring.

Rule severity, numeric thresholds, exact analyzer configuration and any shared package remain implementation-stage choices that need qualification and repository adoption. No such decision is marked approved here.

Source-aware review against the request and cited code/guidance was performed while drafting. Mechanical document validation is separate from a runtime quality assessment. No independent unfamiliar-reader study, application test run, performance measurement or refactoring was performed for this plan.

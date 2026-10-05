---
title: "Language defaults and complete testing harness plan"
type: implementation-plan
status: Draft
version: "0.1"
scope: "Product:ChangeImpactStudio"
owner: "Andrew Spiteri"
last_reviewed: "2026-10-05"
review_cadence: "before implementation and at each milestone"
cis:
  stable_id: change-impact-studio:plan:testing-harness-defaults
---

# Language defaults and complete testing harness plan

## Outcome and current authority

CIS should guide each solution towards a complete, maintainable testing harness comparable in assurance to PARR. When a repository has no selected framework, CIS should recommend an established language-appropriate framework, show how to assemble the applicable test layers, and expose gaps before feature work is described as verified. The harness includes test-mode instrumentation: correlated logs, metrics and traces for diagnosis, plus comparable performance evidence that can reveal regressions.

This is a planning proposal requested on 5 October 2026. No CIS implementation, active standard, framework selection, package, CI configuration, or application harness is changed by this document. Implementation and migration exercises remain subsequent work. This document is not an approved CIS delivery plan.

The scope covers SC-05 through SC-12 and the testing aspects of SC-13 through SC-16 in the [CIS assurance improvement candidates](../references/cis-assurance-improvement-candidates-2026-10-05.md). It does not claim to repair application capacity failures, refactor production code, or close those findings by publishing guidance.

All qualification examples must be synthetic and reproducible from public CIS fixtures. This proposal does not rely on application-specific source or artifact evidence.

The related [code quality and architecture plan](code-quality-architecture-plan.md) uses this harness for structural enforcement, composition tests and later behavior-preserving refactoring. Shared discovery and classification work should be delivered once. Production refactoring requires the relevant test and instrumentation baseline; optional shared-library work does not block harness delivery.

## What the investigation established

| Finding | Evidence | Planning consequence |
| --- | --- | --- |
| PARR supplies concrete framework guidance and a broad assurance model. | PARR's [testing pyramid](C:/miscwork/portfolio/PARR/.github/instructions/testing-pyramid.instructions.md), [testing standard](C:/miscwork/portfolio/PARR/docs/generic/standards/testing-standard.md), [architecture policy](C:/miscwork/portfolio/PARR/docs/generic/policies/architecture-test-policy.md), and [security instructions](C:/miscwork/portfolio/PARR/.github/instructions/security-testing.instructions.md). | Preserve the useful defaults and depth of the harness while translating product-specific rules. |
| CIS already has layered suites, native result adapters, traceability, security, workflow and assurance capabilities. | [Testing module](../../src/Cis.Modules.Testing/TestingModule.cs), [contracts](../../src/Cis.Abstractions/CisTestingContracts.cs), and [earlier delivery plan](parr-testing-delivery-implementation-plan.md). | Extend and connect existing capabilities; do not create another test execution framework. |
| Starter discovery mainly binds existing .NET projects and Node scripts. | [RepositoryTestingStarter](../../src/Cis.Modules.Repository/RepositoryTestingStarter.cs), `AddDotNet` and `AddNode`. | Add an explicit missing-harness recommendation and readiness path. |
| Initialization can label solution-wide .NET execution as unit tests. | `AddDotNet` uses the solution shortcut when no browser project is detected; other initialization paths also default non-browser .NET projects to unit. Import has a different, conservative layer heuristic. | Share classification logic between import and initialization; preserve unknown or mixed scope rather than infer pure unit coverage. |
| An empty repository can retain empty classification and omit implementation standards as it grows. | [standard selector](../../src/Cis.Modules.Repository/DefaultStandardRegistry.cs). | Reassess source/classification drift before planning implementation, including repositories initialized before their first source file. |
| Current test validation checks profile shape and result contracts but does not establish complete harness readiness. | [TestingService](../../src/Cis.Modules.Testing/TestingService.cs), `ValidateProfile`; [doctor check](../../src/Cis.Modules.Repository/TestSuiteProfileDoctorCheck.cs). | Keep syntax validation distinct from assessment of applicable layers, framework selection, and execution evidence. |
| Earlier PARR-equivalence closure was demonstrated with Friends Todo. | [Gap matrix](parr-testing-delivery-gap-matrix.md), evidence boundary and test-suite matrix. | Retain that historical evidence; add empty-repository, unclassified-growth and representative migration fixtures before claiming generic onboarding coverage. |
| PARR has reusable logging test support, operation timing, metrics, configurable diagnostic streams and browser diagnostics. | [Logging instructions](C:/miscwork/portfolio/PARR/.github/instructions/backend-observability-logging.instructions.md), [recording log](C:/miscwork/portfolio/PARR/src/backend/tests/support/PARR.Tests.Observability/RecordingLog.cs), [metric recorder](C:/miscwork/portfolio/PARR/src/backend/building-blocks/PARR.BuildingBlocks.Observability/Metrics/IMetricRecorder.cs), [diagnostic configuration](C:/miscwork/portfolio/PARR/src/backend/building-blocks/PARR.BuildingBlocks.Observability/ServiceCollectionExtensions.cs), and [browser fixture](C:/miscwork/portfolio/PARR/src/tests/regression/PARR.BrowserRegression.Tests/Infrastructure/PlaywrightFixture.cs). | Treat instrumentation activation, capture, correlation and interpretation as harness capabilities. Trace exact test/host wiring during implementation; these source examples do not prove every PARR suite enables every signal. |

This was targeted source and documentation review, not an exhaustive implementation audit. No new application tests were executed. Existing routing cards were stale and used only to locate source. Current tool documentation supports the recommendations below, but the proposed tool combinations have not been qualified together in this work.

## 1. Selection policy

The proposed selection order is:

1. Preserve the repository's explicit, current framework selection and stronger test policies.
2. Discover existing tools and distinguish observed use from an approved or otherwise explicitly adopted repository choice. Do not replace a working NUnit, MSTest, Jest, Node test runner, unittest, or other established harness merely because it differs from the default.
3. Where no choice exists, recommend the CIS default for the component's language, runtime, application type and execution environment. Show the expected layers, alternatives, prerequisites and unsupported capabilities.
4. Record adoption or an alternative and its rationale through the existing repository/technical-intent workflow. Once the relevant choice and implementation scope are authorized, do not introduce a separate approval for every test project.
5. Treat a new home-grown assertion/discovery/reporting framework as a deviation requiring a concrete justification and capability assessment. Domain fixtures, fault-injection helpers, native standard-library runners, and thin invocation wrappers are legitimate; they are not automatically bespoke test frameworks.

Language selects a starting point, not the complete policy. A .NET library, .NET service, .NET CLI and .NET service with a TypeScript UI need different compositions. Browser harness ownership is selected at solution level so that two language profiles do not create duplicate browser suites.

Do not classify a project as bespoke merely because `OutputType` is `Exe` or it is run through `dotnet run`: xUnit v3 and Microsoft Testing Platform support executable test projects. Inspect framework/package/platform evidence and discovery/reporting behavior. See [xUnit's platform documentation](https://xunit.net/docs/getting-started/v3/microsoft-testing-platform).

## 2. Proposed language defaults

Deliver .NET first, then TypeScript/JavaScript, then Python. The catalogue must be extensible, but this proposal does not label unqualified languages as fully supported. Existing native, mobile, infrastructure, JVM, Go or Rust repositories retain their adopted tools; absent qualified defaults produce an explicit recommendation gap rather than a generic fabricated runner.

| Concern | C# / .NET | TypeScript / JavaScript | Python |
| --- | --- | --- | --- |
| Unit and component framework | xUnit; qualify a pinned xUnit v3/platform combination for new projects and preserve existing supported selections. | Vitest; preserve established Jest or Node test-runner choices. | pytest; preserve established unittest choices. |
| Coverage | A collector supported by the selected .NET test platform, with native coverage output and a validated CIS adapter. Do not assume VSTest and MTP flags are interchangeable. | Vitest's compatible coverage provider and reporter. | pytest-cov / coverage.py. |
| Mutation | Stryker.NET over selected high-risk pure logic. | StrykerJS with the matching native runner plugin. | mutmut where supported; explicitly record platform prerequisites and qualify its evidence adapter. |
| Architecture | NetArchTest.Rules as the initial default; ArchUnitNET or existing deterministic checks are valid alternatives. | dependency-cruiser plus framework-specific boundary checks where needed. | Import Linter plus targeted structural checks where needed. |
| Integration | Native framework, Testcontainers for real dependencies, and the appropriate application-host fixture. | Native framework, Testcontainers, and the actual application/API composition. | pytest fixtures, Testcontainers, and the application's supported test client or host. |
| Business acceptance | Reqnroll with the selected supported test runner. | Cucumber.js for business-readable cross-step scenarios. | pytest-bdd for business-readable cross-step scenarios. |
| Browser journeys | Playwright for .NET with the selected supported runner. | Playwright Test. | Playwright's pytest integration when Python owns the browser harness. |
| API compatibility and regression | Common solution-level contract and regression requirements below, using native test hosts and CIS API evidence. | Same requirements, native tools. | Same requirements, native tools. |

These are proposed recommendations, not a package installation manifest. Each released recipe must pin and test compatible framework, platform, collector, mutation tool, fixture and browser versions together; document supported operating systems and retain alternative recipes where required. A catalogue update must not silently upgrade an adopted repository.

The catalogue should record profile ID/version, language/runtime, application shape, recommended tools, accepted alternatives, supported result formats, prerequisites, example location and last qualification evidence. Recommendation, template availability and executed qualification are separate support states.

## 3. What a complete harness must account for

Every applicable capability needs an owner, suite or check binding, command, prerequisites, expected evidence, execution tier and completion rule. Inapplicability needs a concrete reason. Missing, blocked and unverified must remain visible.

| Capability | Required behavior and evidence |
| --- | --- |
| Unit | Isolated deterministic assertions for rules, boundaries, invalid inputs and denied paths. No real network, database, browser or child process dependencies. |
| Component | Actual component/host composition and serialization, with explicit boundaries and a separate result identity from pure unit tests. |
| Mutation | Challenge high-value calculations, authorization, lifecycle and historically fragile logic; report scope, score, surviving mutants and dispositions. A model review must not silently satisfy an adopted mutation requirement. |
| Architecture | Test dependency direction, ownership and relevant structural/security rules. Demonstrate a deliberately introduced forbidden dependency being detected. |
| Integration and migrations | Real representative dependencies, deterministic isolated state, readiness, schema setup, transactions, concurrency, retries, rollback and cleanup. Testcontainers is provisioning, not a test layer. |
| API and contract compatibility | Export the actual contract; compare with the retained supported baseline; test status/error shapes and relevant consumer behavior. Schema validity alone is not compatibility or behavioral correctness. Include CLI and event contracts where applicable. |
| Business acceptance | Cross-step scenarios in domain language; thin bindings to real application behavior; outcomes beyond individual methods. |
| Regression | Preserve named defect reproductions and stable requirement identities in the appropriate layers; verify those identities executed. Regression is a purpose across layers, not an excuse to relabel every test as a separate layer. |
| Frontend component and accessibility | Exercise interaction, accessible names, focus, validation and state where a frontend exists. |
| Browser automation | Critical composed journeys, enabled actions and persisted outcomes, page/component objects, deterministic identities, traces, screenshots and sanitized logs. Keep separate from backend jobs. |
| Security behavior | Negative unit, architecture, API/integration and browser cases at the affected trust boundaries. Test authorization, isolation, validation and disclosure; avoid inventing tenant/authentication concepts a product does not have. |
| Security scanning | Applicable SAST, dependency, secret, configuration, image and DAST evidence through the existing security module. Preserve adopted CodeQL gates. Keep scanners distinct from security behavior tests and account for authenticated and unauthenticated DAST surfaces. |
| Operational, fault and capacity | Startup, process isolation, cancellation, crash/recovery, backup/restore and representative resource limits where relevant. Small synthetic tests do not establish production-shaped capacity. |
| Test-mode instrumentation | Deliberately enable the declared log, metric and trace collectors in test composition; prove activation and retain correlated diagnostics, including partial output on failure or timeout. Pure unit fixtures use in-memory capture. |
| Performance regression | Compare selected application/workload metrics against a compatible retained baseline under declared resources and instrumentation. Distinguish regression, pass, missing evidence and inconclusive comparison. |
| Coverage and independent assurance | Changed-code coverage, reviewed exclusions, baseline non-regression, and the independent techniques required by risk or repository policy. Preserve implementer/assurer and run provenance. |

Proposed numerical starting points follow existing CIS/PARR direction: 95% changed-production line coverage, with approved bounded exceptions; selected release mutation scopes use high 85, low 80 and break 80, retaining stronger existing settings. PR mutation can begin as an explicit advisory baseline and ratchet toward its agreed enforcing tier. For high-risk changed behavior, the plan must state whether mutation blocks completion; an advisory run is not a release pass. Scores do not substitute for meaningful assertions.

For the CLI-only example application, browser and frontend-component layers are inapplicable while the product remains CLI-only. CLI/service journeys, API compatibility, business scenarios, exact arithmetic, real PostgreSQL, architecture, process faults, resource limits and security remain relevant. Local trusted APIs need the documented local exposure controls, not imported PARR-specific Auth0 or tenant rules.

## 4. Proposed CIS behavior and ownership

### Discovery and repository growth

- Assess current source evidence alongside saved classification. Detect implementation added after empty initialization without relying solely on the old component list.
- When intended stack is explicitly selected before source exists, offer its harness recipe; when the stack is unknown, report that choice as unresolved. Do not guess from unrelated files.
- Distinguish the unit framework, execution platform, command, reporter, fixture provisioning and test layer. `dotnet test` is an invocation, not proof of xUnit or unit scope.
- Recognize centrally managed/shared .NET packages, imported project properties and configured package scripts using bounded static inspection. Do not execute repository code merely to discover it.
- Reconcile changed standards/guidance/profile candidates through the existing preview and ownership rules. Preserve edited files and reviewed commands; expose conflicts and missing capabilities.
- An implementation repository with no behavioral harness must not look ready because a documentation-only fallback row exists. A documentation-only repository must not receive a compulsory application harness.

### Canonical configuration and derived assessment

Extend the existing `references/test-suite-profile.md` contract with versioned framework selection and capability/applicability data; keep concrete suites and native commands there. Reference the existing security-suite profile and API baseline records rather than copying their authority into a second inventory.

Track three separate facts: the recommendation, the adopted configuration, and the evidence that it ran. A syntactically valid profile proves only that its schema and bindings are valid. An assessment should expose missing framework selection, missing applicable layer, unknown/mixed layer, missing adapter, unverified recipe, stale classification and unavailable prerequisites separately.

Prefer extending `cis test validate` and `cis repo doctor`, with a focused recommendation/assessment projection under the existing `cis test` module if needed. Exact additional CLI names remain an implementation-design decision. All projections require consistent human, JSON and agent output. Read-only assessment must not install packages, overwrite profile files, refresh approved baselines or run tests.

Older profiles remain readable. Missing new metadata initially produces actionable diagnostics; after a repository adopts the new harness policy, mandatory readiness/completion checks enforce it. Generic schema validation must not silently become a breaking policy gate for every existing installation.

### Examples and agent steering

Ship versioned examples and deterministic templates through the existing generation/starter path. Link them from bootstrap, technical-intent, implementation, testing and verification guidance. The prompt should direct an agent to inspect or establish the harness before adding feature tests, use the selected native runner, and report unimplemented applicable capabilities.

Examples live in maintained source/template directories. Reusable test helpers live under repository test/tooling paths; attempt logs, binaries and reports remain disposable evidence. Do not create one-use versioned execution scripts as the default location for maintainable harness code.

The guidance must show both a new repository and an existing non-default framework. It must explain when a full reference example is useful and when only a smaller applicable subset should be scaffolded. Scaffold creation alone cannot produce a verification pass.

### Test-mode instrumentation and diagnostic evidence

The user explicitly requires the PARR pattern of additional instrumentation enabled during tests. Add a declared instrumentation profile to each relevant harness recipe and bind its identity/digest to execution evidence. Extend the existing [diagnostics profile](../references/diagnostics-profile.md), workflow capture and test-artifact correlation; avoid a second telemetry database or a mandatory vendor service.

The fixture or test host must activate collection before the scenario begins and record the effective settings and collector readiness. A profile declaration without observed signals is not proof that instrumentation was enabled. Required missing signals produce an instrumentation-evidence gap; they must not be interpreted as zero errors, zero latency or successful performance. Functional results remain separately visible when diagnostics fail.

| Signal | Proposed test-mode behavior |
| --- | --- |
| Structured application logs | Retain stable event names, outcome/reason, exception classification, operation/phase and elapsed time. Correlate request/trace, test case, suite, run, attempt and source revision in logs or evidence metadata. |
| Operation and dependency timing | Capture handler/job phases and relevant database, HTTP, queue or storage calls, including retries, timeouts and cancellation. Record counts and payload/workload sizes where meaningful so a slow run can be explained. |
| Metrics | Capture application counters, durations/histograms and relevant CPU, memory, allocation/GC, queue-depth, throughput or query metrics. Metrics use their native measurement APIs, not values reconstructed by parsing prose logs. Record unsupported signals explicitly. |
| Traces | Retain request-to-handler-to-dependency relationships and useful async/process correlation. Preserve existing tracing abstractions and native instrumentation; do not require PARR namespaces in other products. |
| Browser diagnostics | Retain action/request timings, console/page errors, network failures, and existing traces/screenshots. Keep browser and backend evidence connected by correlation IDs where supported. |
| Test-runner and environment diagnostics | Preserve fixture setup/teardown, process exit, dependency readiness and cleanup outcomes separately from application execution timings. |

Use bounded capture for ordinary test runs and an explicit deeper diagnostic profile for investigations. Retain compact metrics on successful as well as failing runs for comparison; retain detailed failure logs/traces and a bounded pre-failure context. A deeper diagnostic rerun is a separate attempt and does not erase the first failure.

Capture available telemetry before disposing hosts, containers and browser contexts, with a bounded flush. The parent harness should preserve available child/process output when a child times out or crashes. Report lost buffers, sampling, truncation, export failure and cleanup failure; never promise complete telemetry after a hard kill. Preserve artifacts under the existing attempt-specific diagnostics layout and hash/correlate them during reconciliation.

Test-mode instrumentation must use the real application code paths. It may change collectors, verbosity, sampling and destinations; it must not bypass authorization, change business decisions, disable normal consistency controls or mask exceptions. Pure unit tests use reusable in-memory log/metric/trace sinks and do not acquire a network collector dependency. Integration and composed tests use the actual application instrumentation with local collectors/exporters as appropriate.

Apply the same central redaction and bounded-tag rules as the product. Keep per-test/request IDs in event or artifact correlation rather than unbounded metric labels. Sensitive payloads, credential-bearing requests and raw SQL values are excluded by default. Test telemetry stays local to the declared evidence path unless an external destination is separately authorized. Verify redaction and ensure test-specific verbose/export settings are not accidentally activated in the normal production profile.

PARR's reusable `RecordingLog<T>`, `SensitiveLogAssertions`, timed operation logging, semantic metrics and diagnostic stream are reference patterns. Preserve those tools when importing PARR. For other products, implement equivalent test adapters around their adopted observability stack rather than mandating PARR's `ILog<T>` abstraction. Full production observability redesign is outside this change; missing application instrumentation becomes a bounded prerequisite for the affected harness capability.

### Performance regression assessment

Test duration alone is insufficient. Select representative operations/workloads and compare application latency, throughput, dependency-call counts, retries, allocation/memory and resource use where those metrics matter. Keep fixture startup and teardown separate from the measured operation.

Each performance scenario must declare the dataset/shape, operation count, concurrency, warm-up and measured repetitions, cold/warm-cache state, runtime/dependency versions, resource limits and hardware/runner class. Bind both the baseline and candidate to the same instrumentation profile and sampling settings. Compare compatible environments or explicitly report the result as incomparable; do not turn faster hardware into a product-performance claim.

Record absolute budgets and permitted relative regression before execution. Use adequate repeated samples and an agreed statistic/noise tolerance; percentile claims need enough observations. Treat insufficient or noisy samples as inconclusive. Hard invariant budgets such as excessive database-call counts can be deterministic gates; noisy wall-clock timing from shared CI should not become a brittle unit-test assertion.

Measure or bound instrumentation overhead during recipe qualification. Deep diagnostic runs and coverage/mutation-instrumented builds are not interchangeable with the normal performance baseline. Mutation timeouts measure mutation execution, not application performance. Never silently refresh the baseline to accept a regression; require the existing reviewed baseline-change process and retain the previous evidence.

Performance remains a declared execution tier: lightweight count/budget regressions can run on PRs, with stable representative comparisons on dedicated or controlled runs. Emit a concise delta report linking an observed slowdown to relevant phase, dependency and resource evidence. The report can identify candidates for investigation; it must not invent a causal explanation unsupported by the signals.

## 5. Reference example deliverables

The first reference is a small .NET API with one business workflow, PostgreSQL and an optional small browser client. It is a standalone public example, separate from the qualification fixture. Each example below must include native setup, exact bounded commands, expected artifacts and a controlled failure demonstration, executed later during qualification.

| Example | Proof required |
| --- | --- |
| Unit plus mutation | Boundary tests catch a deliberately changed rule; selected mutants run and survivors remain inspectable. |
| Architecture | A forbidden dependency fails, then the restored boundary passes. |
| Integration | Real transaction rollback, concurrency or retry behavior; unavailable Docker is not passed; cleanup works after an assertion failure. |
| API compatibility | A breaking response/error-contract change is detected against a retained baseline; no automatic baseline refresh. |
| Business | A readable multi-step scenario exercises application behavior and maps to a stable requirement ID. |
| Regression | A known defective variant fails its named regression; the corrected variant passes with the same test identity. |
| Browser and accessibility | Perform and persist an action; exercise denied/error behavior; demonstrate retained trace/screenshot output on failure. |
| Security | A negative access/disclosure fixture plus applicable scanner receipts; missing scanner or credentials remain unavailable. |
| Instrumentation | Observe known operation logs, a metric update and a correlated trace; capture them before cleanup; show redaction and unavailable-export handling. A timeout/crash retains available partial evidence without claiming complete capture. |
| Performance regression | A controlled extra dependency call or workload slowdown is detected against a compatible baseline; normal variance, incompatible environments and absent metrics are not reported as a clean pass. |
| CLI-only variant | Omit browser infrastructure with a reason and run a real CLI-to-service journey with structured output and exit-code assertions. |
| Full delivery | Local and CI commands consume the same suite definitions; final evidence binds results to source revision, run, attempt and suite. |

TypeScript/JavaScript and Python reference recipes use equivalent outcomes rather than identical project structure. Thin adapters may normalize native reports, but must not implement a replacement assertion, discovery or test-running framework. Adapter and OS gaps must be resolved before a recipe is labelled qualified; this is especially relevant to Python mutation tooling.

## 6. Execution tiers and completion

| Tier | Proposed scope |
| --- | --- |
| Local iteration | Smallest meaningful affected layer with native filtering; fast feedback and retained failures. |
| Pull request / equivalent local review | Relevant unit, architecture, component, integration, contract, business, regression and security gates; changed-code coverage; selected mutation and UI journeys according to impact. Test-file and harness changes must trigger their owning checks. |
| Scheduled / extended | Broader mutation, platform matrices, resilience and longer representative workloads where agreed. Required release evidence cannot be replaced by an old scheduled success. |
| Release | Required lower-level gates before composed journeys; mandatory mutation/security/operational results, exact artifact identity and freshness checks before promotion. |

Use the existing workflow runner, test reconciliation, security reconciliation, API compatibility and verification composition. Keep code at one source revision and bind release evidence to the actual promoted artifact where applicable. Retain the first failure and distinguish diagnostic reruns; a retry must not silently erase flaky or failed canonical evidence. Missing results, zero discovered tests where cases are required, skipped mandatory cases and stale results cannot establish completion.

The example application need not acquire a remote CI provider as part of harness selection. Its local verification entry point should provide the same classified gates and be ready for an explicitly selected CI environment later.

## 7. Bounded implementation sequence

All tasks below are proposed and not started. Order puts the prevention of another missing-harness outcome before migrating the example application.

| Task | Depends on | Bounded change | Acceptance evidence |
| --- | --- | --- | --- |
| TH-01: catalogue and profile contract | — | Finalize default precedence, initial language profiles, capability model, support states, compatibility and adoption semantics. Map every PARR capability to a portable requirement or explicit product-specific exclusion. | Reviewed schema/examples; no conflation of framework, runner, layer, recommendation and execution. |
| TH-02: .NET discovery and growth | TH-01 | Fix empty-to-source drift, native framework recognition and import/init layer consistency. Preserve central/shared project configuration and meaningful unknowns. | Fixtures for new .NET, old empty profile, xUnit executable, NUnit/MSTest, mixed solution, wrapper and genuine custom runner. |
| TH-03: standards and steering | TH-01, TH-02 | Carry testing/security starters and harness recommendations into init/import/reconciliation and relevant agent guidance without overwriting repository choices. | Clean and human-edited profile fixtures; idempotent rerun; explicit proposed additions and conflicts. |
| TH-04: readiness assessment | TH-01, TH-02 | Add capability/selection diagnostics to test validation and doctor, with backward-compatible schema behavior and adopted-policy enforcement. | Documentation-only, CLI-only, API/browser, absent framework, missing layer, unavailable prerequisite and valid alternative cases. |
| TH-12a: instrumentation contract | TH-01 | Define per-suite activation, correlation, signal expectations, limits, redaction, evidence identity and test/production profile separation. Inspect PARR's actual test-host wiring. | Effective settings and signal readiness distinguish enabled, missing, unsupported and failed collection; no production-policy bypass. |
| TH-12b: capture lifecycle | TH-12a | Add reusable native test sinks/collectors and bounded export hooks to the .NET example and existing diagnostics/evidence path. | Known logs/metrics/traces captured; timeout and collector failures retain available evidence; parallel cases stay isolated; cleanup and redaction verified. |
| TH-12c: performance evidence | TH-12a, TH-12b | Add compatible-baseline comparison, scenario/resource identity, predeclared budgets and delta reporting using existing workflow/verification mechanisms. | Controlled regression detected; incompatible/noisy/missing evidence is explicit; no automatic baseline acceptance; instrumentation overhead is recorded. |
| TH-05: .NET reference and templates | TH-01, TH-03, TH-12b | Build the .NET examples in section 5, including CLI-only composition and test-mode instrumentation; qualify exact package/platform combinations. | Clean restore/build; each applicable layer executes; intentional defects are detected; generated commands and artifacts agree. |
| TH-06: TypeScript/JavaScript recipe | TH-01, TH-03, TH-12a | Equivalent native-framework examples, architecture, business, mutation, browser and instrumentation composition; preserve adopted alternatives and package managers. | Clean native execution, reports and telemetry reconciled, non-default framework preservation and controlled failure cases. |
| TH-07: Python recipe | TH-01, TH-03, TH-12a | Add discovery/standards support where missing, native pytest/coverage/business/integration and instrumentation recipes with qualified mutation/architecture evidence handling. | Linux/Windows capability declarations match actual runs; unsupported platform, signal or result adapter is explicit. |
| TH-08: delivery composition | TH-04, TH-05, TH-12c | Wire adopted readiness to planning/verification and local/CI tiers; preserve traceability, coverage, mutation, API, security, instrumentation and performance authority. | Missing required layer blocks completion; no-output/zero-test/stale/failed/skipped evidence is rejected; approved inapplicability remains distinct. Repeat with TH-06/07 as they land. |
| TH-09: upgrade and import regression | TH-03, TH-04, TH-08 | Exercise fresh, existing, partially configured, custom and multi-language repositories; qualify starter upgrades. | No tool switching or destructive overwrite; existing supported commands still work; gaps stay visible rather than becoming synthetic passes. |
| TH-10: representative fixture migration pilot | TH-05, TH-08, TH-09 | In a separately authorized change in the example application, inventory every existing case/helper, adopt the reviewed .NET harness and migrate representative suites before the remainder. | Stable case mapping, meaningful assertion parity, real PostgreSQL/fault tests preserved, coverage/mutation/architecture/contract evidence added; CLI browser inapplicability documented. |
| TH-11: closure and publication | TH-06, TH-07, TH-09, TH-10 | Update capability/qualification docs, release guidance and the gap matrix with exact demonstrated scope. | Fresh examples and upgrade checks pass; unresolved platform and example-application issues remain named; no unsupported universal-equivalence claim. |

TH-05, TH-06 and TH-07 must each be delivered as smaller native-framework/fixture, assurance-layer, and qualification changes. Do not attempt a single large multi-language implementation. The pilot migration can start after the .NET path is qualified without waiting for the Python recipe, but closure must accurately state the language scope delivered.

## 8. Likely implementation surfaces

These locations were inspected or identified for follow-up; the list is an impact starting point, not a compiler-verified complete dependency graph.

- Repository: `RepositoryClassifier.cs`, `DefaultStandardRegistry.cs`, `RepositoryTestingStarter.cs`, `RepositorySecurityStarter.cs`, `TestSuiteProfileDoctorCheck.cs`, `RepositoryStarterBinder.EngineeringAssurance.cs`, `ImplementationSkillPackRegistry.cs`, and init/import reconciliation callers in `src/Cis.Modules.Repository/`.
- Testing: `src/Cis.Abstractions/CisTestingContracts.cs`, `src/Cis.Modules.Testing/TestingService.cs`, `TestingModule.cs` and `TestResultAdapters.cs`. Preserve result-adapter extension boundaries.
- Diagnostics: `src/Cis.Modules.Diagnostics/DiagnosticsService.cs`, the diagnostics profile, workflow capture and `TestingService.SuiteDiagnosticArtifacts`. Preserve native instrumentation and add only the metadata/adapters needed for test correlation and comparable performance evidence.
- Delivery: existing generation/template, workflow, API, security, plan and verification owners. Resolve exact callers and gates before changing behavior; do not add a second authority or new top-level harness module.
- Tests: `tests/Cis.Modules.Repository.Tests/RepositoryInitializerTests.cs`, `MinimalImportTests.cs`, existing testing-service tests, and focused cross-module delivery/upgrade fixtures.
- Documentation: testing/security standards and conformance mappings; suite profiles; implementation skill packs; engineering-assurance coverage; generated guidance and command manuals; the earlier PARR gap matrix with its historical scope retained.

Before implementation, establish a fresh graph baseline, generate the governed feature/dossier and bounded task records, and resolve any remaining contract choices. This planning pass did not refresh graph/index state or create approvals. Do not claim a runtime or framework change from this document's registration.

## 9. Migration fixture safeguards and non-goals

The later pilot should inventory the custom runner's test identities, fixtures, subprocess protocols, reports and fault injection before migration. Preserve every meaningful assertion and retained historical result. Split compound cases where this improves failure localization; case counts may increase, so compare behavior and identities rather than raw totals alone.

Migrate pure arithmetic first, then architecture and contract checks, then PostgreSQL/component/business and process/fault suites. Replace manual registration/reporting with native runner capabilities while retaining justified domain-specific helpers. Use a temporary, explicitly tracked coexistence period and retire old commands only after parity and replacement invocation are verified. Carry existing dataset phase timings into the declared test instrumentation profile; collect operation, dependency and resource evidence around failures without changing execution budgets. Establish a comparable performance baseline before claiming regression protection.

Do not change production business behavior, deployment, business policies, installed services or resource budgets as a harness migration. Representative capacity failures stay failures. PARR's auth provider, database choice, cloud runner paths, credentials, tenancy rules and CI organization are not portable defaults. Security and performance tests must be adapted to the example application's actual authority and environment.

## 10. Decisions proposed for review

The recommended initial decisions are: .NET first; TypeScript/JavaScript and Python follow; existing established frameworks take precedence; capability-complete language recipes are versioned and qualified; source-growth reassessment precedes readiness; test-mode logs/metrics/traces and compatible performance comparisons are part of the harness; examples demonstrate failures as well as passes; adopted missing requirements block completion; a synthetic .NET fixture is the first migration exercise.

Exact package versions, .NET execution platform/collector pairing, any additional CLI surface and rollout of stricter profile checks must be settled during TH-01/TH-05 using qualification evidence. The user can revise the proposed defaults before implementation; no choice is represented here as approved.

## Tool references checked for this proposal

Primary documentation was consulted on 5 October 2026. These links support tool capabilities, not proof of the full recipe's compatibility or execution.

- [xUnit platform behavior](https://xunit.net/docs/getting-started/v3/microsoft-testing-platform), [Stryker.NET framework support](https://stryker-mutator.io/docs/stryker-net/technical-reference/testing-framework/), [Reqnroll xUnit integration](https://docs.reqnroll.net/latest/integrations/xunit.html), [Testcontainers xUnit fixtures](https://dotnet.testcontainers.org/test_frameworks/xunit_net/), [NetArchTest](https://github.com/BenMorris/NetArchTest), and [Playwright .NET runners](https://playwright.dev/dotnet/docs/test-runners).
- [Vitest reporters](https://vitest.dev/guide/reporters), [StrykerJS Vitest runner](https://stryker-mutator.io/docs/stryker-js/vitest-runner/), and [dependency-cruiser rules](https://github.com/sverweij/dependency-cruiser/blob/main/doc/rules-reference.md).
- [pytest output](https://docs.pytest.org/en/stable/how-to/output.html), [pytest-cov reports](https://pytest-cov.readthedocs.io/en/stable/reporting.html), [pytest-bdd](https://pytest-bdd.readthedocs.io/en/latest/index.html), [mutmut](https://mutmut.readthedocs.io/en/latest/), and [Import Linter contracts](https://import-linter.readthedocs.io/en/stable/contract_types/).

Review limit: source-aware comparison with the user's request and the cited repository guidance was performed while drafting. No independent reader study, toolchain compatibility run, package installation, runtime test or migration was performed.

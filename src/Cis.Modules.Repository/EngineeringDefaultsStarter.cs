namespace Cis.Modules.Repository;

/// <summary>Portable guidance, separate from project commands and from proof that those commands ran.</summary>
internal static class EngineeringDefaultsStarter
{
    internal const string Policy = """
        {
          "schemaVersion": 1,
          "requireTaskCompletion": true,
          "additionalGates": []
        }
        """;

    internal static string Guide(string repositoryId) => $$"""
        ---
        title: "Engineering Defaults and Native Harness"
        type: reference
        status: Active
        owner: "Repository maintainers"
        last_reviewed: "2026-10-05"
        review_cadence: "on stack, capability, tool or policy change"
        cis:
          stable_id: {{repositoryId}}:reference:engineering-defaults
        ---

        # Engineering defaults and native harness

        ## Adoption and language choices

        Prefer an established native harness equivalent in assurance depth to PARR. CIS coordinates evidence;
        it is not a test runner. Preserve working project choices and stronger standards. An empty repository
        has no test framework until one is selected and installed. Reassess when source appears.

        | Language | Starting choice when none is adopted | Review before adoption |
        | --- | --- | --- |
        | C# / .NET | xUnit v3, native `dotnet test`, Coverlet, Stryker.NET; SDK analyzers and SonarAnalyzer.CSharp | Preserve NUnit, MSTest or another capable harness. Confirm VSTest versus Microsoft.Testing.Platform, collector compatibility and exact pinned versions. |
        | TypeScript / JavaScript | Vitest, framework-native ESLint/TypeScript, StrykerJS, Playwright | Preserve Jest and adopted alternatives. Recipe qualification is pending. |
        | Python | pytest, coverage.py, Ruff, an appropriate native mutation tool | Preserve adopted tools. Recipe qualification is pending. |

        Pin selected versions in the existing package/tool manifests. A central package version alone does not
        establish that any project uses it. Conditional MSBuild imports require evaluated configuration review.
        Never replace native discovery/assertions with a home-made console test loop or report generator.

        ## Local reference examples

        C# initialization copies the bundled reference harness to `.cis/local/examples/dotnet-engineering/`.
        Read its `README.md` and `.github/skills/example-replay/SKILL.md` for native testing, test instrumentation,
        explicit Core/Persistence/CLI responsibilities and agent-facing commands. This ignored reference copy is
        excluded from project discovery and execution identity. It does not install a framework in your application.
        Initialization never restores packages, starts containers or runs tests. Follow the example README explicitly.
        `cis repo init --root <docs-root> --examples off` disables future installation and updates; `--examples auto`
        re-enables them. Both choices persist in `.cis/example-settings.json`; disabling preserves existing copies.
        Preview reconciliation with `--dry-run`. Unchanged files can update; edited or unowned files remain intact
        with warnings. `.cis/local/examples/manifest.json` records the available bundle digest and each last-applied
        file hash; it is not proof that a customized copy matches the bundle or passed tests. Export a fresh local
        copy with `cis repo example --destination .cis/local/example-comparison` to compare retained edits.

        ## Full assurance inventory

        | Gate | Expected evidence and examples |
        | --- | --- |
        | unit / component | Native discovery with meaningful assertions; isolate nondeterministic boundaries; zero discovered cases cannot pass. |
        | coverage / mutation | Actual collector and mutation reports scoped to changed production behavior. Starting policy: 95% changed-production line coverage; release mutation high 85, low 80, break 80. Preserve stronger policy; advisory PR mutation is not a release pass. |
        | architecture | Native tests for owned boundaries and dependency direction, including a deliberate violating fixture; zero matched types is not success. |
        | integration / migrations | Real isolated dependencies, lifecycle cleanup, concurrency/retry/rollback and schema migration checks. |
        | api-compatibility | Consumer-visible signature/schema compatibility; a deliberately breaking change must fail. |
        | regression | Reproduction of previous defects with behavioral assertions. |
        | business | Thin scenario bindings over application use cases; representative dataset, complete results/readback, declared limits, recovery and installed behavior. |
        | browser | Native Playwright or the adopted harness, persisted journey, denied/error cases, keyboard/accessibility, retained failure trace. A CLI-only scope records inapplicability. |
        | security | Negative behavior at trusted boundaries plus applicable dependency, secret, static, configuration and dynamic scans; scanners do not replace behavior tests. |
        | instrumentation / performance | Correlated test logs, metrics and traces; bounded capture, partial failure artifacts and compatible baseline comparison. |
        | lint / format | Native analyzers and formatter invoked with declared severity and scope; prove seeded violations are detected. |

        Register only real commands and output paths in `test-suite-profile.md` and the workflow. A configured
        command is not a pass. Result bindings do not define the complete required inventory. Review additional
        project standards and task requirements even when every configured check is green.

        ## .NET recipe procedure

        1. Select the SDK, test framework, runner/platform and collector as a compatible set. Use central package
           management where already present. Keep `PrivateAssets="all"` on analyzer dependencies.
        2. Separate projects or native categories for unit, architecture, integration, business and browser work.
           Share deterministic fixtures and application hosting, not a second assertion or discovery engine.
        3. For a VSTest project, verify `dotnet test <project> --logger "trx;LogFileName=<suite>.trx" --results-directory <output>`.
           Microsoft.Testing.Platform uses its own qualified extensions/options; do not copy VSTest arguments blindly.
           Multi-target projects need unique output per target to avoid overwriting results.
        4. When Coverlet is selected, verify `--collect "XPlat Code Coverage"` and discover the actual generated report.
           Do not declare a guessed coverage path. A verified `.cis/local/<attempt>/*/coverage.cobertura.xml` selector
           accepts exactly one immediate collector directory and avoids VSTest's nested deployment copy.
        5. Restore the pinned local Stryker tool and run bounded mutation on changed production code. Inspect survivors,
           no-coverage mutants and timeouts; do not silently exclude difficult code or accept a degraded baseline.
           Qualify the mutation runner independently: xUnit v3 needs the Stryker MTP recipe in the public native example;
           a successful VSTest/Coverlet run does not establish mutation compatibility. Preserve failed qualification attempts.
        6. Run native analyzers during build; run `dotnet format <solution> --verify-no-changes --no-restore` separately.
           Configure exact enforced diagnostic severities; installed analyzers with nonblocking warnings are not a gate.
        7. Use a real PostgreSQL container/database for persistence acceptance where applicable, isolated per run. Test
           precise value conservation, deterministic replay, duplicate/out-of-order events and stop/restart recovery.
           Missing Docker/database/browser/scanner prerequisites remain missing, not passed or inapplicable.
        8. Demonstrate an intentional failure, then restore the passing case. Record SDK, runner, collector, analyzer,
           database and browser versions plus artifact identity. Imported inferred bindings are unverified until this runs.

        Inspect the bundled native recipe with `cis repo example --format json`; export to a new directory using
        `cis repo example --destination examples/native-harness --dry-run`, then repeat without `--dry-run`.
        This copies a reference solution, not an adopted framework or a ready project. Existing paths are never overwritten.

        For changed C# coverage, explicitly review `.cis/coverage-scope.json` with `schemaVersion: 1`, an exact
        `baseRevision` Git commit and `productionPaths` containing every applicable production source directory.
        CIS projects native Cobertura hits onto Git changes, including untracked source. Missing source coverage counts
        against the result; zero executable changed lines requires scoped inapplicability, not an invented 100% pass.
        Retain the baseline commit, native report and reviewed scope. Broader language/report projections remain unqualified.

        ## Test-mode diagnostics and performance

        Enable diagnostic detail through explicit test composition. Keep business and authentication behavior unchanged.
        Use run/suite/case/attempt correlation; parallel fixtures must not share mutable capture buffers or databases.
        Capture application logs, counters/histograms and traces with native listeners or the adopted telemetry collector.
        Start collection before the action, drain/dispose on success, timeout and failure, and retain available partial evidence.
        Bound bytes, duration and retention; sanitize before persistence/sharing and never capture credentials.
        Keep the first failure when retrying. Record collector unavailability and incomplete capture explicitly.

        Business tests own representative acceptance. Declare dataset shape/size, concurrency, resource limits, full-result
        assertions and performance budgets before execution. Compare only matching workload, software, machine/resource,
        runtime and instrumentation identities; report capture overhead. Missing/incompatible/noisy evidence cannot pass a
        required performance gate. Baseline changes require review; a smaller workload cannot replace the representative run.
        The bundled BenchmarkDotNet example emits unchanged native full JSON. Adopt reviewed budgets and a hash-bound
        baseline in `.cis/performance-policy.json`; `cis plan performance-check --candidate <report> --workflow <state>`
        checks native measurements, compatibility and current execution without updating the baseline. See its command manual.

        ## Readable code and adaptable architecture

        Give each class/function a cohesive responsibility and recognizable name. Organize by meaningful ownership so a
        maintainer can find a use case, its dependencies and failure paths. Prefer explicit constructor dependencies and
        composition-root wiring when substitution is needed. Do not require a DI container, an interface for every class,
        or a shared framework in an existing design. Reuse a common package only when boundaries and consumers justify it.
        For example, separate message enrichment, formatting, destination writing and lifecycle configuration; avoid one
        service that also parses options, performs I/O, retries and renders reports. Conversely, splitting every trivial
        expression into another interface/class worsens navigation. Review the responsibility, not the number of files.
        Aggregate partial declarations when judging type complexity. Pair lint and boundary tests with a source-based
        readability review of callers, cancellation, errors, state and replacement points.

        ## Application CLI and portable skills

        Default to an application CLI exposing supported use cases through the same application services as other adapters.
        Use a native parser, help/discovery, noninteractive commands, stable exit codes and versioned JSON with bounded output;
        keep diagnostics on stderr. Declare effects, authentication, cancellation, idempotency and dry-run semantics where useful.
        A CLI must not bypass authorization or reach directly into storage as a shortcut. Developer build commands alone do
        not satisfy an application interface. Libraries can document a justified alternative or inapplicability.

        Maintain portable skills for discovery, a representative workflow, verification and diagnosis. Each records prerequisites,
        exact commands, input/output contracts, side effects and failure handling. Route agent-specific entry points to the same
        canonical skill. Validate command/schema drift and exercise the workflow through two supported integrations before
        claiming portability; do not hardcode an agent provider.

        ## Closing every iteration

        1. Before reusing task context, compare relevant source/configuration identities and refresh intervening changes.
        2. At task end, rebuild the affected graph. Review limitations; a fresh graph does not prove complete caller context.
        3. Reassess source capabilities and CIS dependency/definition versions. Preview repository reconciliation, preserve
           human-owned files and adopted tools, and apply only authorized changes. Never perform a blind package upgrade.
        4. Reassess all applicable standards, instructions, skills and checks. Include absent gates; resolve conflicts and
           unsupported adoption explicitly. Refresh graph and affected checks after canonical/configuration changes.
        5. Connect current scoped requirements to tasks, implementation, test cases/results and review dispositions. Later
           product phases remain visible without becoming obligations of an unrelated current task or granting activation.
        6. Use the assigned independent reviewer in a separate review run through the selected provider adapter. Supply source,
           relevant callers/contracts, operational consequences, complete evidence and explicit omissions. Expand to source
           when a summary is inaccurate. Missing material context blocks review; a graph rebuild alone cannot repair it.
        7. Close only when every applicable required gate passes. Failed, missing, stale and skipped block. Inapplicable is a
           separate justified disposition, not a pass. Unresolved blocking findings and edits after review invalidate closure.

        `.cis/engineering-defaults.json` versions adoption. Existing projects without this policy retain their established
        lifecycle until reviewed adoption with `repo init --adopt-engineering-defaults` (preview with `--dry-run`).
        Empty repositories adopt the defaults on initialization. Declaring a stack alone does not adopt gates in an existing repository.
        After adoption, deleting the managed policy or setting `requireTaskCompletion` to false blocks completion. Managed updates use the starter manifest; preserve human customizations and expose
        conflicts. This guide and generated bindings are configuration, not qualification or an assertion that tests ran.

        For an adopted governed plan, `cis plan task completion-context <change-id> <task-id>` prints a JSON template with
        the independently required gates and current identities. Save the reviewed evidence under the existing change
        dossier at `verification/<task-id>.json`. Populate gate states, concrete rationale and repository-relative artifacts
        with `sha256:` digests; identify distinct implementation/reviewer runs. The receipt also requires an `implementation` artifact binding the successful native implementation manifest and result to this task contract and final source identity, or an explicit human implementation record. The reviewer must start after implementation and use a separate provider session. Native test gates require a current CIS test
        manifest, build/lint/format require the corresponding named workflow step, and review requires the provider run
        manifest. Semantic gates also need source-based reviewer assessment, not a file-existence claim. The template
        starts every gate missing and never approves completion. Implementation build and unit gates cannot be marked inapplicable. Coverage inapplicability requires a current passing native test manifest with changed-production scope, a retained base revision, zero measured executable changed lines and zero uninstrumented changed files, bound to its own coverage report. This permits comment-only edits in instrumented files while missing instrumentation still blocks. Policy fields `minimumCoverageLines` and `minimumMutationScore` preserve stronger thresholds above the 95/80 minimums. `cis plan task transition ... --status Complete` validates it.
        """;
}

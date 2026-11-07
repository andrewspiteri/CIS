namespace Cis.Modules.Repository;

internal sealed record ImplementationSkillDefinition(
    string Name,
    string Description,
    string Content);

internal sealed record SelectedImplementationSkillPack(
    string Id,
    string Title,
    string Reason,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<ImplementationSkillDefinition> Skills);

internal static class ImplementationSkillPackRegistry
{
    public static IReadOnlyList<SelectedImplementationSkillPack> Select(
        RepositoryClassification classification)
    {
        var selected = new List<SelectedImplementationSkillPack>();

        AddWhen(selected, classification, "dotnet-quality", ".NET quality",
            "C# implementation was detected.",
            component => component.Languages.Contains("csharp", StringComparer.Ordinal),
            [
                Skill("dotnet-test", "Build and test affected .NET projects with bounded commands and preserved evidence. Use when changing C# or .NET code.",
                    "Verify .NET changes without turning a targeted edit into an unbounded solution-wide run.",
                    [
                        "Identify affected projects, adopted framework, native runner/platform and direct test projects; inspect inherited configuration. When no harness is adopted, consult `.cis/local/examples/dotnet-engineering/README.md` when installed and the engineering-defaults reference. Verify compatibility before adoption; examples are reference material, not project implementation or passing evidence.",
                        "Run the native formatter and SDK/Sonar analyzers or adopted alternatives with declared blocking severities.",
                        """
                        Build the narrowest project boundary with warnings visible.
                           Demonstrate ordinary native discovery and test execution on the supported
                           installation; for applicable Windows/.NET work include normal Visual Studio
                           Run All. Record an unavailable route and its completion implication explicitly.
                        """,
                        "Match execution to the checkpoint: ordinary edits use affected builds/focused native tests; coherent batches use affected regression and one review; documentation-only corrections use documentation/generation checks; mutation fixture/adapter changes require a small correctness pilot; feature candidates require full affected-module mutation and applicable checks; release candidates retain all required families/platforms/compatibility/package checks. State the behavioral outcome, relevant tests, time/storage budget and stop rule. Record justified inapplicability separately.",
                        "Enable correlated test-mode logs, metrics and traces without changing business/authentication behavior; retain partial evidence and first failures.",
                        """
                        Record exact commands, versions, source identity, results, skipped checks, unavailable prerequisites and residual risk. Assess required gates independently of the configured command list before completion.

                        For supported CIS native mutation/incremental execution, preserve validated
                        inherited `CIS_WORK_*` authority, especially its parent. Otherwise use the existing
                        approved owner with `--change`, `--task`, `--actor` and, for linked targets,
                        `--authority`. Read `cis agent work status <change> --task <task>` before expansion;
                        profile limits and `--max-seconds` tighten the finite allocation rather than grant
                        time or attempts. Read-only plan/status/evidence inspection remains available.

                        A new or changed mutation runner/fixture path needs the retained representative native
                        controls in `cis test mutation run --kind canary --profile <pilot.json>`, followed
                        by the existing controller's `agent work progress` assessment before governed
                        mutation expansion. Link its `native-pilot:<basis>` evidence. Configure
                        `correctnessPilotProfile` on the target mutation/incremental mutation profile and retain the
                        separate per-run positive-kill canary. Synthetic protocol fixtures are fault tests,
                        not evidence of actual mutant activation. Follow the two native command manuals.
                        Normalize ignored session values consistently at actual pilot, ordinary,
                        canary and postflight launches, preserving declared overrides, validated
                        CIS_WORK authority and identity from the original declared inputs.
                        """,
                    ],
                    "Do not report a build as proof that behavior or architecture tests passed.",
                    completionEvidence:
                    """
                    Preserve affected scope, exact commands, outcomes, generated artifacts, skipped checks, and residual risk in the canonical task or verification record.
                    Separate measured warm, edited-code and completed-regression duration, including
                    preparation/native/result-admission cost and monitoring overhead. Do not sum
                    overlapping parent/child time or report an unfinished cutoff as a completed run.
                    Keep owner/attempt coordinates out of material test identity; reuse compatible
                    reports and reviewed producer revalidation while preserving historical cost and
                    independent recovery holds. Inspect total owned/protected/reserved storage before
                    expansion; local output caps do not authorize an independent storage allowance.
                    Check each original operation growth grant separately from the shared owned
                    ceiling. Required Finish/settlement failure holds returned and retained overall
                    outcomes until legitimate reconciliation; a next-operation hold after successful
                    settlement or an active ancestor alone does not invalidate settled evidence.
                    """),
            ], declared: classification.DeclaredStack == "csharp");

        AddWhen(selected, classification, "core-testing", "Core testing",
            "Production implementation behavior was detected.",
            HasImplementation,
            [
                Skill("add-unit-tests", "Add focused unit tests that prove behavior and edge cases without coupling to implementation details. Use when changing deterministic logic.",
                    "Create fast tests that explain the contract and fail for meaningful regressions.",
                    ["Map acceptance criteria and invariants to observable cases.", "Follow the repository test framework, naming, and fixture conventions.", "Cover success, boundary, invalid, and authorization-sensitive behavior as applicable.", "Run the smallest affected test selection and inspect the failure mode.", "Record test evidence in the task or verification record."],
                    "Do not add tautological tests, sleep-based synchronization, or assertions on private implementation structure."),
                Skill("add-architecture-tests", "Add deterministic architecture tests for module boundaries and dependency rules. Use when a .NET change creates or crosses architectural seams.",
                    "Protect intended dependencies and ownership boundaries as executable policy.",
                    ["Read ADRs, module ownership, and existing architecture-test conventions.", "Express one stable boundary rule per test.", "Include a deliberate negative fixture when the framework permits it.", "Run the architecture-test project and preserve the command output.", "Link the rule to its governing decision or specification."],
                    "Do not encode transient namespaces as architecture without a canonical rule."),
                Skill("add-mutation-tests", "Use mutation testing to challenge important unit-test assertions. Use when risk or independent assurance requires evidence beyond line coverage.",
                    """
                    Find behavior that tests execute but do not meaningfully assert, after checking
                    that the native mutation runner and fixtures can execute the selected scope.
                    """,
                    [
                        """
                        Read the adopted mutation profile, threshold and stronger evidence rules. Fix
                           source/configuration/tool identities, scope, wall time, maximum diagnostic
                           attempts, artifact budget and free-space reserve before execution.
                           Use `cis test mutation run --profile <path> --kind diagnostic` for a bounded
                           native iteration and `--kind qualification` for the complete declared profile.
                           Keep relevant tests/build/tool configuration in the profile's input pins; target
                           details belong in profiles rather than new mutation controllers.
                           When compilation dominates, group sources with a shared compilation and compatible
                           test context in one native invocation. Use the generic profile command above or
                           the documented direct native route; the incremental service currently selects
                           mutation work one source file at a time. Include zero-definition files in scope
                           without a separate compilation for each.
                           For governed work, preserve inherited `CIS_WORK_*` authority and its live parent,
                           or pass the existing `--change`, `--task`, `--actor` and linked `--authority`.
                           Inspect `cis agent work status <change> --task <task>` and total owned storage;
                           profile limits never allocate new time, attempts or bytes. Missing ownership
                           holds before preparation. Non-adopted diagnostics remain explicitly unaccounted.
                        """,
                        """
                        Qualify a small representative canary through the native mutation runner: healthy
                           baseline passes; an intentional mutant produces the intended failed test. Include
                           a real child-process/transport path when relevant, with actual cleanup. Record
                           selected and executed case counts and elapsed time. Verify that early bail
                           reduces the remaining queued work; a stop request alone does not prove saved
                           work. If the adapter continues running queued cases, stop and repair test
                           selection or adapter behavior, then measure again before a broad campaign.
                           Stop on a failed prerequisite before any broad campaign.
                           Also demonstrate a known survivor/control, rejection of an infrastructure failure
                           and healthy restored execution. Qualify coverage-analysis and early-bail changes
                           on the actual adapter/async path. A positive kill count alone is insufficient.
                           Estimate the full-run budget from the pilot before expansion.
                           Retain these controls through the existing `--kind canary` route with a
                           representative profile's `correctnessPilot` definition; use
                           `correctnessPilotProfile` on the target diagnostic/qualification/incremental
                           profile. The first actual outcome needs the existing controller's substantive
                           `agent work progress` assessment, including `native-pilot:<basis>` evidence,
                           before expansion. This is routine controller reflection, not a new human gate.
                           Native reports, actual launch references and relevant runner/adapter/fixture/
                           configuration/tool/environment inputs determine currentness; a Boolean or
                           synthetic report cannot establish governed readiness. Compatible proof remains
                           reusable across unrelated production edits. Unknown or mismatched native paths
                           hold rather than borrowing a different runner's proof. Keep the ordinary narrow
                           positive-kill canary separate; a child round trip qualifies only the actual path
                           observed and does not prove arbitrary cross-process mutant activation.
                        """,
                        """
                        Use native early bail by default. Distinguish runner-recorded intentional bail
                           from timeout, crash, cancellation and missing protocol evidence. Do not require
                           every selected test to execute after a mutant has been detected.
                        """,
                        """
                        Run one bounded diagnostic hypothesis at a time. Investigate shared setup,
                           selection fan-out, host startup and cleanup when execution dominates. Stop at the
                           declared iteration budget or a repeated failure without new information.
                        """,
                        """
                        Compare distinct mutant outcomes in the same source scope. Diagnostic row counts
                           are not additional kills. Review survivors against exact source and contracts;
                           never invent equivalence, exclusions or accepted limitations.
                        """,
                        """
                        Record native counts, conservative assessment and execution health separately.
                           Normal native assertions and failed-test exceptions need no bespoke whitelist or
                           every-row direct-assertion proof; preserve actual infrastructure failures and any
                           stronger adopted policy.
                           Keep `validEvidence`, `thresholdMet` and `qualified` distinct. A valid report at
                           the unchanged threshold may remain unqualified under a genuine required-child,
                           infrastructure, restoration or cleanup hold. Incremental receipts and partial
                           diagnostics always remain unqualified. Native assertion-message changes or an
                           alternative legitimate detecting test do not erase a native kill.
                           Required Finish/settlement failure preserves these native facts but holds
                           returned and retained overall outcomes until legitimate reconciliation.
                           A next-operation hold after successful settlement, or a settled child's
                           active ancestor, does not invalidate that settled evidence.
                        """,
                        """
                        After focused improvements and a healthy runner, execute the full required scope
                           for the final gate with its unchanged threshold. A canary, partial run or timeout
                           cannot satisfy that gate.
                        """
                    ],
                    """
                    Do not hide survivors, relax thresholds or convert timeouts into kills. Preflight
                    scratch and retained-storage caps; stop rather than accumulating archives or
                    deleting protected evidence without authorization. Do not repeat a broad campaign
                    until the failed prerequisite or hypothesis changes. Stronger adopted policy wins.
                    """,
                    completionEvidence:
                    """
                    Link a compact record of source commit/dirty diff, command/configuration, tool
                    versions, native report and hashes, distinct status counts, execution failures,
                    unrun checks and residual risk. Keep native reports unchanged. Add bounded failure
                    diagnostics only when needed; source/runtime copies and per-mutant controllers are
                    not routine evidence.
                    Bind relevant production, test, build/configuration and tool inputs, including
                    reproducible relevant untracked inputs. For disjoint native groups, retain one
                    unchanged report per group and apply the adopted complete-scope threshold to summed
                    outcomes rather than averaging percentages. Repository receiving rules must verify
                    complete scope and current inputs before any pilot report contributes to that gate.
                    Include platform identity in one compact candidate manifest. Keep timeouts and
                    unresolved misses explicit without kill credit.
                    Use compact candidate-bound miss rows naming the definition/operator/span/replacement,
                    raw status, requirement, decision and authorized reviewer; an approval Boolean alone
                    does not establish review authority. Keep these rows in one reviewed miss table.
                    Successful captures are transient: discard them after assessment. Keep failed
                    diagnostics only while useful for an unresolved issue, then remove resolved,
                    superseded and duplicate attempts under the repository retention policy. A later
                    failed or running attempt must not leave an older passing score looking current.
                    Record preparation, native execution and result-admission costs separately, with
                    warm and edited-code increments distinct from completed affected regression.
                    Overlapping parent/child durations are not additive and an unfinished cutoff is
                    not a completed measurement. If restoration is unresolved, resume only the original
                    pinned recipe under its retained work reference; `--recovery-of` requests bounded
                    restoration and healthy postflight, not another canary or campaign. Restored health
                    cannot manufacture missing correctness observations.
                    Exact verified original restoration may pass a repeated-blocker exploration hold
                    within its existing recipe/reserve/attempt; preserve that hold and history for
                    subsequent exploration. Normalize ignored session values at all actual native
                    launches while preserving declared overrides and the original material inputs.
                    """),
            ]);

        AddWhen(selected, classification, "business-assurance", "Business acceptance assurance",
            "Application or user-facing behavior was detected.",
            HasApplicationBehavior,
            [
                Skill("add-business-acceptance-tests", "Add acceptance-style tests for cross-step business outcomes and lifecycle workflows. Use when behavior spans commands, states, actors, or repositories.",
                    "Prove reviewed acceptance behavior in domain language without turning scenarios into low-level implementation scripts.",
                    ["Select approved acceptance criteria that cross more than one operation or state transition.", "Express scenarios using actors, preconditions, actions, outcomes, and denied paths.", "Keep step or fixture code thin and delegate setup through supported application boundaries.", "Use deterministic identities, clocks, and data while avoiding live human authentication.", "Run the focused acceptance suite and preserve scenario-level evidence."],
                    "Do not replace focused unit or integration tests with broad acceptance scenarios."),
            ]);

        AddWhen(selected, classification, "api-integration", "API integration",
            "A backend API producer was detected.",
            component => component.Roles.Contains("backend-api-producer", StringComparer.Ordinal),
            [
                Skill("add-api-integration-tests", "Add API integration tests for contracts, authorization, persistence boundaries, and errors. Use when changing a backend endpoint or API behavior.",
                    "Prove the externally observable API contract against the real application boundary.",
                    ["Start from the API dictionary, OpenAPI contract, permissions, and problem catalogue.", "Exercise success, validation, missing-resource, conflict, and authorization cases as applicable.", "Use the repository application factory or supported integration harness.", "Assert status, headers, schema, important values, and prohibited disclosure.", "Run deterministic contract validation with the targeted integration tests."],
                    "Do not bypass middleware or substitute controller-unit tests for API integration evidence."),
            ]);

        AddWhen(selected, classification, "data-persistence", "Data and persistence",
            "Persistence or database ownership was detected.",
            component => component.Capabilities.Contains("persistence", StringComparer.Ordinal)
                || component.Roles.Contains("database", StringComparer.Ordinal),
            [
                Skill("database-migration", "Design, implement, and verify reversible schema migrations and backfills. Use when persistent schema or stored data must change.",
                    "Deliver data changes with explicit compatibility, rollout, rollback, and recovery evidence.",
                    ["Compare the canonical data model with the current schema and usage.", "Separate schema migration from large or risky data backfill work.", "Define forward compatibility, rollback limits, locking, and dataset-size assumptions.", "Test apply and rollback or compensating recovery on a representative database.", "Record deployment ordering, monitoring, and reconciliation queries."],
                    "Do not combine irreversible destructive changes with application rollout without explicit human approval."),
                Skill("review-schema-sql", "Review schema and SQL changes for correctness, safety, performance, and operability. Use when queries, indexes, constraints, or migrations change.",
                    "Detect data correctness and production-operability risks before rollout.",
                    ["Trace each change to a data requirement and owning component.", "Review nullability, constraints, indexes, cardinality, and concurrency assumptions.", "Inspect representative query plans or explain output for performance-sensitive SQL.", "Check migration duration, locks, retries, rollback, and backfill observability.", "Record findings with exact file and evidence references."],
                    "Do not approve a migration solely because it applies to an empty local database."),
            ]);

        AddWhen(selected, classification, "real-dependency-testing", "Real dependency testing",
            "Persistence, authentication, eventing, or another service boundary was detected.",
            component => component.Capabilities.Any(capability => capability is "persistence" or "authentication" or "events")
                || component.Roles.Any(role => role is "database" or "event-producer" or "event-consumer"),
            [
                Skill("testcontainers-integration-test", "Add disposable real-dependency integration tests using the repository container harness. Use when service behavior cannot be proven with substitutes.",
                    "Verify integration behavior against a production-shaped dependency without shared mutable test state.",
                    ["Confirm that a real dependency is required and classify the test as component or integration, never unit.", "Reuse the repository container fixture and a pinned dependency image or module version.", "Create isolated state and deterministic seed data per test boundary.", "Wait on a service readiness signal instead of a fixed sleep.", "Exercise the real protocol, serialization, queries, constraints, or transaction behavior that motivated the container.", "Dispose resources and preserve the targeted test command plus container-runtime prerequisites."],
                    "Do not require a developer-owned shared service, silently fall back to a fake, leak credentials, or report the test as passed when container execution did not run."),
            ]);

        AddWhen(selected, classification, "frontend-delivery", "Frontend delivery",
            "A web, mobile, native, or game frontend was detected.",
            IsFrontend,
            [
                Skill("frontend-implementation", "Implement reviewed frontend behavior using the repository shell, design system, and platform conventions. Use after wireframes and designs are approved.",
                    "Translate approved designs into accessible, consistent, testable product behavior.",
                    ["Confirm the design gate is approved and read the effective UI framework profile.", "Reuse the application shell and shared controls before adding components.", "Implement loading, empty, error, permission, responsive, and navigation states.", "Preserve public, customer, and backoffice surface boundaries.", "Run platform-appropriate unit, interaction, rendering, and navigation checks."],
                    "Stop implementation when design approval is absent, rejected, stale, or hash-mismatched."),
                Skill("add-frontend-component-tests", "Add component and interaction tests for user-facing states, accessibility semantics, and emitted actions. Use when frontend components or screens change.",
                    "Prove component behavior below the browser-journey layer while preserving the platform's rendering and accessibility conventions.",
                    ["Map approved screen states and actions to focused component cases.", "Render with the repository framework and realistic typed inputs.", "Exercise user actions through accessible roles, labels, and platform interaction APIs.", "Cover loading, empty, validation, denied, failure, disabled, and responsive behavior as applicable.", "Assert observable output and emitted navigation or commands, then run the focused component suite."],
                    "Do not assert private component state, brittle DOM depth, or snapshots without behavioral assertions."),
                Skill("accessibility-review", "Review user-facing changes for keyboard, assistive technology, contrast, semantics, and motion. Use for web, mobile, native, or game UI delivery.",
                    "Make the reviewed interface usable beyond its default pointer-and-visual path.",
                    ["Map controls and states to semantic roles, labels, focus order, and announcements.", "Verify keyboard or platform navigation without hidden traps.", "Check contrast, text scaling, reduced motion, errors, and non-color cues.", "Run available automated checks and complete a manual interaction pass.", "Record limitations and ownership for unresolved accessibility risk."],
                    "Do not treat an automated accessibility scan as complete assurance."),
                Skill("frontend-observability", "Add privacy-safe frontend diagnostics, performance signals, and release-health evidence. Use when user-facing behavior needs production detection or diagnosis.",
                    "Make client failures and degraded journeys diagnosable without collecting sensitive interaction data.",
                    ["Identify critical journeys, failure states, performance thresholds, and owning teams.", "Reuse the repository telemetry provider, consent, sampling, and environment conventions.", "Capture stable error and navigation identities without secrets, tokens, form values, or sensitive URLs.", "Correlate client and server activity where supported and bound high-cardinality dimensions.", "Verify dashboards or queries, source maps, release identity, and alert ownership."],
                    "Do not add session capture, personal data, or verbose production logging without explicit privacy approval."),
            ]);

        AddWhen(selected, classification, "browser-assurance", "Browser assurance",
            "A web frontend framework was detected.",
            component => IsWebFrontend(component) || component.Frameworks.Contains("playwright-dotnet", StringComparer.Ordinal),
            [
                Skill("add-browser-regression-tests", "Add stable browser regression tests for approved user journeys and visual states. Use when web frontend behavior or rendering changes.",
                    "Prove high-value browser journeys while keeping selectors and visual evidence maintainable.",
                    ["Read .github/instructions/cis-engineering-assurance.instructions.md and the repository's approved browser harness; preserve .NET Playwright or Node Playwright as adopted.", "Start from approved wireframes, design manifests, routes, and acceptance criteria.", "Reuse deterministic app composition, isolated seeded data, page objects and production-isolated test authentication.", "Use semantic selectors and observable readiness rather than sleeps or unconditional network-idle waits.", "Exercise changed actions through their resulting mutations, saved values, navigation and error states; presence alone is not coverage.", "Cover navigation, loading, empty, error, authorization, disabled, keyboard and responsive states as applicable.", "Keep visual snapshots bounded to reviewed components or screens.", "Run the supported browser matrix in its correct CI tier, preserve failure artifacts and explicitly report unavailable prerequisites."],
                    "Do not update snapshots merely to make an unexplained difference pass."),
            ]);

        AddWhen(selected, classification, "secure-delivery", "Secure delivery",
            "Authorization or externally reachable API behavior was detected.",
            component => component.Capabilities.Contains("authorization", StringComparer.Ordinal)
                || component.Roles.Contains("backend-api-producer", StringComparer.Ordinal),
            [
                Skill("secure-feature", "Threat-model and verify authorization, exposure, validation, and secret-handling changes. Use when a feature crosses a trust boundary or handles sensitive data.",
                    "Make security requirements explicit and testable before completion is claimed.",
                    ["Read .github/instructions/cis-engineering-assurance.instructions.md and the applicable security standard and suite profile.", "Identify actors, principal and tenant scope, assets, trust boundaries, entry points, and abuse cases.", "Map permissions to server-side enforcement and negative tests; isolate test authentication from production.", "Check validation, output filtering, logging, secrets, caching, and rate limits.", "Maintain real configuration and environment contracts without inventing entries for pure fixtures.", "For public endpoints, verify cached projection access and no direct database dependency.", "Preserve required CodeQL or other SAST, dependency, secret and DAST checks and exact human-approved exception rules.", "Update tests and documentation together, then record security evidence, residual risk, and independent review requirements."],
                    "Do not rely on hidden UI controls or client-side checks as authorization."),
            ]);

        AddWhen(selected, classification, "infrastructure-delivery", "Infrastructure delivery",
            "Infrastructure or deployment assets were detected.",
            component => component.Roles.Contains("infrastructure", StringComparer.Ordinal)
                || component.Frameworks.Contains("terraform", StringComparer.Ordinal),
            [
                Skill("infrastructure-delivery", "Implement and validate infrastructure, configuration, secret, and pipeline changes. Use when delivery requires cloud, Terraform, queue, index, or CI/CD work.",
                    "Make environment changes reproducible, reviewable, least-privileged, and recoverable.",
                    ["Trace the change to an approved task and environment ownership.", "Reuse modules and naming, tagging, secret, and state conventions.", "Format, initialize without backend mutation when possible, validate, lint, and scan.", "Review the plan for replacement, privilege, cost, and data-loss risk.", "Document rollout, rollback, drift detection, and post-deployment verification."],
                    "Do not apply infrastructure or expose secret values as part of an implementation-only task."),
            ]);

        AddWhen(selected, classification, "operational-readiness", "Operational readiness",
            "A backend API, worker, event producer, or event consumer was detected.",
            component => component.Roles.Any(role => role is "backend-api-producer" or "worker" or "event-producer" or "event-consumer"),
            [
                Skill("observability-implementation", "Implement actionable logs, metrics, traces, alerts, dashboards, and runbook updates. Use when changed behavior needs operational detection or diagnosis.",
                    "Ensure operators can detect, explain, and recover from the changed behavior.",
                    ["Identify success, failure, latency, saturation, and business-health signals.", "Use stable event names and low-cardinality dimensions.", "Propagate correlation without logging secrets or sensitive payloads.", "Define alert intent, thresholds, ownership, and runbook actions.", "Verify telemetry locally or in a safe environment and preserve evidence."],
                    "Do not add noisy logs or metrics that have no operational consumer."),
            ]);

        AddWhen(selected, classification, "dependency-and-release", "Dependency and release",
            "Package, build, or deployment metadata was detected.",
            component => component.Capabilities.Contains("packages", StringComparer.Ordinal)
                || component.Capabilities.Contains("deployment", StringComparer.Ordinal)
                || component.Roles.Contains("package-producer", StringComparer.Ordinal),
            [
                Skill("dependency-upgrades", "Upgrade dependencies in bounded batches with compatibility, security, and verification evidence. Use when package versions or lockfiles change.",
                    "Reduce dependency risk without mixing unrelated upgrades or obscuring generated changes.",
                    ["Classify the upgrade as security, required compatibility, maintenance, or optional.", "Read release notes and identify breaking, runtime, license, and transitive changes.", "Update manifests and lockfiles with the repository package manager.", "Run affected build, tests, analyzers, and packaging checks.", "Record deferred upgrades and residual compatibility risk."],
                    "Do not bundle unrelated major upgrades into feature work without explicit scope."),
                Skill("release-rollout", "Plan and verify staged release, feature-flag, compatibility, rollback, and post-release checks. Use when a change affects deployment or published packages.",
                    "Turn implementation evidence into a controlled and reversible release path.",
                    ["Define artifacts, environments, compatibility window, and release ownership.", "Specify flags, staged exposure, migration ordering, and rollback criteria.", "Confirm observability and support readiness before increasing exposure.", "Run post-release validation against acceptance and operational signals.", "Record release evidence, incidents, rollback decisions, and follow-up work."],
                    "Do not infer production approval from plan or design approval."),
            ]);

        return selected.OrderBy(pack => pack.Id, StringComparer.Ordinal).ToArray();
    }

    private static void AddWhen(
        ICollection<SelectedImplementationSkillPack> selected,
        RepositoryClassification classification,
        string id,
        string title,
        string reason,
        Func<RepositoryComponentClassification, bool> predicate,
        IReadOnlyList<ImplementationSkillDefinition> skills,
        bool declared = false)
    {
        var components = classification.Components.Where(predicate).ToArray();
        if (components.Length == 0 && !declared)
        {
            return;
        }

        var evidence = components
            .SelectMany(component => component.Evidence.Select(item => $"{component.Id}: {item}"))
            .Distinct(StringComparer.Ordinal)
            .Take(12)
            .ToArray();
        selected.Add(new SelectedImplementationSkillPack(id, title,
            components.Length == 0 ? "Maintainer-declared C# stack; implementation and test tooling remain unverified." : reason,
            components.Length == 0 ? [DeclaredEngineeringStack.Path] : evidence, skills));
    }

    private static bool IsFrontend(RepositoryComponentClassification component)
        => component.Roles.Any(role => role is "frontend-consumer" or "mobile-client" or "native-frontend")
            || component.Frameworks.Contains("godot", StringComparer.Ordinal);

    private static bool IsWebFrontend(RepositoryComponentClassification component)
        => component.Roles.Contains("frontend-consumer", StringComparer.Ordinal)
            && component.Frameworks.Any(framework => framework is "angular" or "nextjs" or "react" or "vue" or "nuxt");

    private static bool HasImplementation(RepositoryComponentClassification component)
        => component.Roles.Any(role => role != "test-automation")
            && component.Languages.Any(language => language is "csharp" or "typescript" or "javascript" or "swift" or "kotlin" or "gdscript");

    private static bool HasApplicationBehavior(RepositoryComponentClassification component)
        => component.Roles.Any(role => role is "backend-api-producer" or "worker" or "frontend-consumer" or "mobile-client" or "native-frontend" or "event-producer" or "event-consumer")
            || component.Frameworks.Contains("godot", StringComparer.Ordinal);

    private static ImplementationSkillDefinition Skill(
        string name,
        string description,
        string purpose,
        IReadOnlyList<string> workflow,
        string guardrail,
        string completionEvidence = "Preserve affected scope, exact commands, outcomes, generated artifacts, skipped checks, and residual risk in the canonical task or verification record.")
    {
        var steps = string.Join("\n", workflow.Select((step, index) => $"{index + 1}. {step}"));
        var content = $"""
            ---
            name: cis-{name}
            description: {description}
            ---

            # CIS {ToTitle(name)}

            ## Purpose

            {purpose}

            ## Workflow

            {steps}

            ## Completion evidence

            {completionEvidence}

            ## Guardrails

            {guardrail}
            """;
        return new ImplementationSkillDefinition(name, description, content + "\n");
    }

    private static string ToTitle(string value)
        => string.Join(' ', value.Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
}

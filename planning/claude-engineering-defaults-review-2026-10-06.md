# Claude review of engineering-defaults changes

The original user-requested review on 6 October 2026 reported four major and nine minor findings. Those historical reports are preserved below. Corrections and further review are recorded in the [dispositions](engineering-defaults-review-dispositions.md). The latest focused review, RUN-20261006080539-BA54C9D174, reports ready with no reproducible source defect and only optional test additions/scope observations. Native verification and project-trial readiness remain separate checks.

The review used a fresh snapshot of the isolated codex/cis-engineering-defaults worktree against baseline f4ede76e99d068235e9ce7156bf5562f1d8c64a8: 56 modified tracked paths and 155 new files, with 1,555 context files initially supplied. The omitted example .gitignore was supplied before the second pass. Claude used read-only tools, ran no commands or tests, edited no implementation files and granted no governance approval. Existing test results were supplied as reported evidence, not independently reproduced.

## Major findings to address first

| Reference | Concern |
| --- | --- |
| Pass 1 / TASK-REV-001 | A managed-policy edit can disable completion and alignment checks without a governed opt-out record. |
| Pass 1 / TASK-REV-002 | The implementer run ID is never verified against an actual implementation record. |
| Pass 2 / TASK-REV-001 | Routine initialization can adopt mandatory completion gates in existing repositories despite guidance promising reviewed adoption. |
| Pass 2 / TASK-REV-002 | The severity normalizer rejects legitimate SARIF/CodeQL severity vocabulary as invalid evidence. |

The candidate remains **not ready for the new-project trial**. Review findings add to the known second executable agent-skill integration and full packaged phased-closing qualification gaps. Review coverage is substantial but not exhaustive; each pass identifies material omissions below.

## Pass 1: revise

Run: RUN-20261006041715-A2F7D70501. Provider: 2.1.284 (Claude Code).

Read-only, time-bounded review of the user-requested-review snapshot against baseline f4ede76e. I ran no commands and changed no files; the reported test, build and audit results are taken as stated, not reproduced. Recommendation: revise. I found two major completion-enforcement defects. (1) In a repository where CIS manages the engineering policy, editing requireTaskCompletion to false quietly turns off task completion, story completion and the alignment check. Deleting the policy file is detected; this edit is not. (2) The implementer run ID on a completion receipt is never checked against a real run. The only check is that it differs from the reviewer run ID. Minor findings: (3) gates marked inapplicable only need an artifact whose hash matches, with no check of what it contains, and this is allowed for every implementation gate, including build, unit, coverage and security; (4) the completion check hard-codes 95% coverage and an 80 mutation score instead of reading them from policy; (5) one missing or unsafe artifact stops the check of all remaining gates; (6) the native-evidence check gives one generic error and drops the specific reason a binding failed; (7) a story participant whose files change during review gets a second result entry, which triggers a misleading count error. Remaining qualification limits: (8) examples/dotnet-engineering/.gitignore is listed in the inventory but missing from the snapshot, so it was not reviewed; (9) the evidence manifests are not authenticated, so the reviewer run is the real check against hand-written ones; (10) the two gaps you already know about remain open. Reviewed: the completion receipt check, native/security/benchmark evidence checks, story completion and its BRD caller, the PlanningService completion path, execution identity and output classification, policy adoption reader, gate-inventory assessment, alignment doctor check, changed-coverage scope and projection, Stryker adapter, workflow run input binding, example export, and the .NET example's core, persistence, web endpoints, diagnostic capture, Stryker config and benchmark entry point. Not reviewed (material): most of the ~5,000-line tracked diff, including SecurityService, AgentService and story-task changes, RepositoryInitializer and starter-binder reconciliation and preservation of customized files, BRD intake, the CLI module wiring, the new docs/standards/skills/instructions, the example's authentication/OpenAPI/browser files and most new tests. I make no claims about those areas, and this result is not an approval.

### Strengths noted by Claude

- Completion fails closed in most paths: missing, failed, skipped, stale (task contract, graph, policy, input digest) and duplicate gates are rejected. Inputs are re-captured after review to catch changes made during the check.
- Execution identity covers uncommitted and new files, rejects linked paths, bounds file counts and sizes, and treats an incomplete Git listing as an error rather than a pass.
- Native evidence binding checks that every suite artifact matches a hashed manifest record, that suites have non-zero totals with no skips, and that coverage scope is changed production only. Mutation needs at least one killed mutant.
- Benchmark review is careful: it checks the environment matches, release build without a debugger, sample count and noise limits, absolute and relative budgets, and that the report was written during a current successful performance step. It never updates the baseline.
- Security acceptances must be specific and expire, and expiry is re-checked at completion time. A passed status with blocking-severity findings is rejected.
- Story completion checks every declared participant exactly once, requires product ownership, and re-checks all participants' input identities after reviewing them all.
- The standalone .NET example keeps responsibilities small (constructor-injected store, telemetry and logger; idempotent append that rejects conflicting content). Test diagnostics are bounded, redacted and journaled per source with CreateNew, and xUnit/TRX stays the source of the pass/fail verdict.
- Example export never overwrites, stays inside the repository and limits file count and size.

### Pass 1 / TASK-REV-001 — major: correctness/completion-bypass

Location: src/Cis.Abstractions/CisEngineeringPolicy.cs:32 (also src/Cis.Modules.Repository/EngineeringAssessmentService.cs:20, src/Cis.Modules.Repository/EngineeringAlignmentDoctorCheck.cs:13, src/Cis.Modules.Plan/EngineeringCompletionReview.cs:29, src/Cis.Modules.Plan/StoryEngineeringCompletion.cs:23-27)

WasManaged is only checked when the policy file is missing. In a repository where .cis/starter-manifest.yml lists .cis/engineering-defaults.json as managed, editing requireTaskCompletion to false gives Adopted=false with no diagnostics. As a result, task completion returns before the receipt is read, story completion records 'not-adopted' with no errors, and the alignment doctor check returns no findings, so the change is not reported anywhere. Trigger: a one-line edit to the policy file, which the implementer can make. Consequence: every gate, the reviewer-run requirement and the alignment check stop applying, without notice. That defeats the protection DeletingAdoptedPolicyCannotSilentlyDisableCompletion (tests/Cis.Modules.Repository.Tests/EngineeringAdoptionTests.cs:47-57) provides for deletion. No test covers this case.

Suggested correction: When the policy was managed, treat requireTaskCompletion=false like deletion: return Adopted=true with a blocking diagnostic, unless a governed, traceable opt-out record exists (for example, an approved decision reference). Also have the alignment check report a managed-policy downgrade. Add negative tests for task and story completion after adoption followed by an opt-out edit.

### Pass 1 / TASK-REV-002 — major: correctness/review-identity

Location: src/Cis.Modules.Plan/EngineeringCompletionReview.cs:55-57

Separation of implementer and reviewer is enforced only as string inequality. ImplementerRunId is never resolved to a run manifest; nothing else in src references it. The reviewer manifest is bound by runId, inputDigest, taskId and contract digest (EngineeringNativeEvidence.cs:30-34), but nothing shows that the implementation run was a different run, provider or session, or that it targeted the same task. Trigger: any receipt with a made-up ImplementerRunId such as 'implementation-1', as used in the tests (EngineeringLifecycleTests.cs:58, EngineeringCompletionTests.cs:228). Consequence: 'separate identified implementation and assigned-reviewer runs' is stated in the error message and the handoff, but is not actually verified. The only verified fact is that a read-only review run succeeded.

Suggested correction: Resolve ImplementerRunId to a retained agent-run manifest (mode implementation, same task ID or contract digest, succeeded) and reject it if the reviewer run has the same run ID or provider session, or if it did not run after the implementer. If implementation may be done by a human, require an explicit 'human-implemented' marker that is reported as such, rather than accepting free text. Add a negative test with an unknown implementer run ID.

### Pass 1 / TASK-REV-003 — minor: correctness/gate-semantics

Location: src/Cis.Modules.Plan/EngineeringCompletionReview.cs:64,80-93; src/Cis.Modules.Repository/EngineeringAssessmentService.cs:35-38

All 18 implementation gates, including build, unit, coverage, mutation and security, are created with AllowsInapplicability=true. An Inapplicable gate needs only a rationale of at least 12 characters and any artifact whose hash matches. EngineeringNativeEvidence runs only for Passed gates, so the artifact's content is never checked. The only remaining check is the reviewer result's 'ready' recommendation. Consequence: for an implementation task, most of the mechanical gate checking can be bypassed by marking gates inapplicable. The plan (EQ-11/EQ-14) expects 'justified inapplicability' to be a separate, evidenced state.

Suggested correction: Do not allow inapplicability for gates that are inherent to implementation tasks (at least build, and the unit/coverage pair when changed production lines exist). Alternatively, require the bound reviewer result to explicitly list and accept each gate marked inapplicable, and check that list in ReviewResultPasses.

### Pass 1 / TASK-REV-004 — minor: correctness/policy-preservation

Location: src/Cis.Modules.Plan/EngineeringNativeEvidence.cs:115,121

The completion check hard-codes changed-line coverage at 95 or more and mutation score at 80 or more. docs/references/engineering-defaults.md:35 describes these as a 'Starting policy' and says to 'Preserve stronger policy'. A stronger mutation break is partly enforced through the adapter's suite status (StrykerJsonResultAdapter.cs:29-36, if cisGate is present). A stronger project coverage threshold is not read at completion, and the 95/80 values cannot be adopted or reviewed as policy.

Suggested correction: Read coverage and mutation thresholds from the adopted engineering or suite policy, with 95/80 as the minimum. Record the threshold that was applied in the gate diagnostics.

### Pass 1 / TASK-REV-005 — minor: correctness/diagnostics

Location: src/Cis.Modules.Plan/EngineeringCompletionReview.cs:90 (caught at :69)

EnsureSafe throws InvalidDataException for a missing, oversized or linked gate artifact inside the per-gate loop. That exception ends the whole review, so later required gates and the native evidence checks are never evaluated. The overall result still fails, so this is not a bypass, but the user sees one generic 'Invalid completion evidence' message instead of the full list of gate failures, which slows correction.

Suggested correction: Record an error for the individual artifact and continue, as the path-escape branch at line 88-89 already does, so all gate failures are reported in one pass.

### Pass 1 / TASK-REV-006 — minor: architecture/readability

Location: src/Cis.Modules.Plan/EngineeringNativeEvidence.cs:13-58

One method handles review, security, test-layer and workflow-step gates through long chains of boolean conditions, skips any non-matching artifact silently, and catches parse errors without logging. Every failure produces the same message (line 57), so the user cannot tell whether the cause was schema version, input digest, reviewer run, contract digest, an unbound artifact or the threshold. The Text and Number helpers are duplicated in EngineeringSecurityEvidence.cs:56-57. Adding new gate kinds means extending the same chains.

Suggested correction: Split this into per-gate-kind validators, each returning the first specific reason it rejected an artifact, and combine those reasons in the final message. Share the JSON accessors.

### Pass 1 / TASK-REV-007 — minor: architecture/readability

Location: src/Cis.Modules.Plan/StoryEngineeringCompletion.cs:58-68; src/Cis.Modules.Brd/FeatureIntakeService.StoryCompletion.cs:37-40

When a participant's input identity changes after all participants have been reviewed, the service adds a second CisStoryCompletionContext for the same repository. The caller's one-result-per-participant check then also appends 'Engineering completion must return exactly one result for every declared participant', which wrongly suggests a service contract defect. The result still fails, but the diagnosis is confusing.

Suggested correction: Add the cross-participant identity error to that participant's existing result entry instead of appending a new one.

### Pass 1 / TASK-REV-008 — observation: review-omission

Location: review-inventory.json:61 (examples/dotnet-engineering/.gitignore)

This file is listed as new in the inventory but is missing from the snapshot, so it was not reviewed. Whether it is in the package's 60 bundled files cannot be determined from the snapshot. Large parts of the scope were also not reviewed in the time available: the SecurityService, AgentService and story-task diffs, RepositoryInitializer and starter-binder reconciliation and preservation of customized files, BRD intake, CLI wiring, the new docs, standards, skills and instructions, the example's authentication, OpenAPI and browser files, and most new tests.

Suggested correction: Add the missing file to the snapshot (or explain why it was excluded) and arrange a focused review of reconciliation/preservation and the SecurityService/AgentService diffs before deciding readiness.

### Pass 1 / TASK-REV-009 — observation: architecture/evidence-trust

Location: src/Cis.Modules.Plan/EngineeringNativeEvidence.cs:18-56; src/Cis.Modules.Plan/BenchmarkEvidenceReview.cs:120-136

Test, workflow, security and review manifests are plain JSON files in the repository. Binding checks only that they are internally consistent and hash-matched to the input digest, and the benchmark freshness check uses file modification times. Someone who can write the repository can create manifests that pass the mechanical checks; the independent reviewer run is the real check against this. The handoff already says hand-written manifests must not be used as acceptance evidence, so this records a limit of the design, not a new defect.

Suggested correction: Document this trust boundary in the completion standard. Optionally, have CIS record a run-ledger digest when it writes each manifest and verify it at completion.

### Pass 1 / TASK-REV-010 — observation: qualification-gap

Location: planning/engineering-defaults-candidate-handoff.md:49-53

Still open, as already stated: the second agent CLI/skill execution, the full packaged multi-phase closing rehearsal, and the EQ-01 to EQ-18 reassessment. These are recorded separately and do not replace the implementation findings above.

Suggested correction: Keep the candidate 'not trial-ready' until these are complete and TASK-REV-001 and TASK-REV-002 are resolved and covered by negative tests.

## Pass 2: revise

Run: RUN-20261006042257-6BF4DAE758. Provider: 2.1.284 (Claude Code).

Read-only follow-up review of user-requested-review, covering the areas the first review left out. I changed no files, ran no commands or tests, started no agents and approved nothing. Recommendation: revise. This is not a readiness certification.

New implementation findings (findings 1–7 in the review list):
1. Major. Running `cis repo init` adds `.cis/engineering-defaults.json` with requireTaskCompletion=true to every repository and records it as CIS-managed. That includes existing repositories that are re-initialized. The engineering-defaults guide says existing projects keep their lifecycle until a reviewed adoption, so this contradicts it. The repo-init manual does not mention the policy.
2. Major. Valid SARIF severity values are now classed as 'unclassified': level `none` and CodeQL's `problem.severity: recommendation` (when no security-severity is present). That marks the whole security suite as invalid evidence, and there is no acceptance route. This conflicts with the starter rule to preserve CodeQL.
3. Minor. The check that a task review left files unchanged hashes only `git status` output. Further edits to a file that was already modified are not detected.
4. Minor. In adopted repositories, a scanner that exits non-zero because it found something (as the example README recommends) is reported as 'not from this successful execution', and its findings are never parsed or recorded.
5. Minor. The bundled example is selected from the working tree by a broad file pattern rather than the tracked inventory. Local output such as TestResults, BenchmarkDotNet.Artifacts or .vs can be packaged, or can push export over its file limits.
6. Minor. Detection of the Microsoft.Testing.Platform runner ignores the SDK/global.json runner settings. As a result, unverified VSTest `--logger trx` commands can be emitted, and Microsoft.Testing.Platform projects are skipped without any warning.
7. Observation. Classification now relies on literal package declarations and skips conditional ones. A test project whose packages are all conditional can fall into the production branches (a warning is emitted).

Previously identified, not re-reviewed and not counted as new: first-review TASK-REV-001 (editing requireTaskCompletion to false silently opts out of completion checks) and TASK-REV-002 (the implementer run ID is never verified) remain open, along with its five minor findings. The two known qualification gaps (second executable skill integration and full packaged phased closure) also remain open. They are listed separately in finding 8.

Areas covered:
- RepositoryInitializer: CreatePlan, PlanManagedArtifact, declared-stack handling.
- Starter binder: engineering-defaults and policy artifacts.
- EngineeringDefaultsStarter, DotNetTestConfiguration, the RepositoryClassifier diff and RepositoryTestingStarter.
- CisEngineeringPolicy.
- The AgentService diff: engineering-review gating, read-only permission requirement, input/contract digests, the review unchanged check, and story review/correction changes.
- AgentService.EngineeringReview and StoryReviewContext.
- The SecurityService and SecurityResultAdapters diffs and their tests.
- CLI wiring: RepositoryModule, RepositoryExampleCommand and the host test.
- Cis.Host.csproj packaging, RepositoryExample export and the newly supplied example .gitignore.
- The example's authentication, web host and endpoints, TestSignals and TestSignalJournal.
- A search of the new guidance for a hard-coded reviewer vendor (none found).

Not covered:
- The example's OpenAPI files (ReadingOpenApi and OpenApiCompatibilityTests), browser tests, ReadingPage.html, WebFixture and SyntheticIdentity.
- The content of EngineeringDefaultStandards.cs and the new skill and instruction files, beyond the starter text and the vendor search.
- The iteration and engineering plan specs.
- The BRD FeatureIntakeService story workflow diff; the PlanModule, PlanningService, Testing and Workflow diffs; AgentService permission recording outside the diff.
- Most new and changed tests.
- I did not check the 10-minute budget.

### Strengths noted by Claude

- Adopted engineering reviews require the plan task-contract reader and read-only permission, and they block if the contract cannot be read (AgentService.cs:249).
- Unknown, NaN and out-of-range scanner severities now fail closed instead of defaulting to medium, and tests cover Trivy and ZAP inputs.
- Adopted security reconciliation records the input digest, acceptances, blocking severities and profile path, and rejects a scanner report written before the run.
- RepositoryExample export never overwrites, stays inside the repository, rejects linked parents and limits file count and size.
- Managed-artifact reconciliation keeps human-owned and edited files and reports a collision instead of overwriting when both the file and the template changed.
- The example's JWT setup validates issuer, audience, lifetime with zero clock skew, the HS256 algorithm and a signing key of at least 256 bits; data endpoints require scope policies.
- The test diagnostic journal is bounded, uses CreateNew per source, flushes as it writes, records collector open and close, and leaves the pass/fail verdict to xUnit/TRX.
- No reviewer vendor is hard-coded in the new engineering guidance or skill-pack text.

### Pass 2 / TASK-REV-001 — major: correctness/adoption-governance

Location: src/Cis.Modules.Repository/RepositoryStarterBinder.cs:171-177; src/Cis.Modules.Repository/RepositoryInitializer.cs:837-846; conflicts with src/Cis.Modules.Repository/EngineeringDefaultsStarter.cs:156-157 and docs/manual/cis_repo_init.md options table

Bind always adds .cis/engineering-defaults.json with requireTaskCompletion=true, and PlanManagedArtifact creates it whenever it is missing. Trigger: re-running `cis repo init --yes` (or a full import) on an existing repository to pick up any template update. Consequence: mandatory completion gates start applying to every in-flight task. The policy is now recorded as managed, so later deleting it is a blocking error (CisEngineeringPolicy.cs:17-21). The guide says existing projects keep their lifecycle until a reviewed adoption, and the repo-init manual does not mention the policy. Existing lifecycle tests have to rewrite the policy to false (DeliveryWorkflowTests.cs:1143-1145, FeatureDeliveryTests.cs:141-147), which confirms the implicit adoption. This is separate from the previously identified opt-out bypass (first review TASK-REV-001).

Suggested correction: Make adoption explicit. Write the policy only for greenfield or declared-stack initialization, or behind an explicit adoption option or a separately confirmed plan item. For existing repositories that lack the policy, report adoption as a pending decision instead of creating the file. Document the effect in cis_repo_init.md. Add a test showing that re-initializing an existing repository without the policy does not enable completion gating unless adoption is confirmed.

### Pass 2 / TASK-REV-002 — major: correctness/security-evidence

Location: src/Cis.Modules.Security/SecurityResultAdapters.cs:45-57 (NormalizeSeverity), :110 and :117-119 (SARIF severity selection); src/Cis.Modules.Security/SecurityService.cs:260

Any severity text outside the fixed map is now 'unclassified', and one unclassified finding makes the whole suite 'invalid-evidence'. Valid SARIF level `none` and CodeQL's rule property `problem.severity: recommendation` (quality queries without security-severity) are not mapped. Trigger: a repository running CodeQL security-and-quality or any SARIF tool that emits level none. Consequence: the security gate fails as invalid evidence for a legitimate scan. ApplyAcceptances returns early for InvalidEvidence, so no governed acceptance can resolve it. This conflicts with the starter rule 'Preserve the repository's required SAST tool, including CodeQL' (RepositoryStarterBinder.EngineeringAssurance.cs:33). The tests cover only Trivy and ZAP unknowns, not valid SARIF values.

Suggested correction: Map SARIF `none` and `recommendation` explicitly (for example to low/info). For SARIF, prefer the numeric security-severity and then the SARIF level over problem.severity. Keep 'unclassified' for values that are actually invalid. Add SARIF and CodeQL tests for level none and problem.severity recommendation.

### Pass 2 / TASK-REV-003 — minor: correctness/review-integrity

Location: src/Cis.Modules.Agent/AgentService.cs:1053; RepositorySnapshot at :2325-2326

For task reviews, the check that the reviewer changed nothing now compares RepositorySnapshot digests. Those are SHA-256 hashes of `git status --porcelain` output only. Trigger: a direct (non-isolated) review of a working tree whose implementation files are already modified, where the reviewer process edits one of those files. The status line (' M path') stays the same, so the run is recorded as Succeeded. Consequence: the run manifest's claim of an unchanged read-only review is not reliable. A completion-time input-digest comparison may catch the edit later, but the run record itself is misleading.

Suggested correction: For engineering and story task reviews, recompute the content-based execution identity (CisExecutionIdentity) after the run and compare it with manifest.InputDigest, or include content hashes of dirty files in the snapshot digest. Add a test where a reviewer edits an already-modified file.

### Pass 2 / TASK-REV-004 — minor: error-handling/diagnostics

Location: src/Cis.Modules.Security/SecurityService.cs:133-142 (compare the non-adopted path at :149-153); examples/dotnet-engineering/README.md:76

In adopted repositories, any bound step that did not succeed makes the suite invalid with the message 'Scanner report is not from this successful execution', and this happens before the adapter runs. Scanners commonly exit non-zero when they find something; the example README itself recommends `gitleaks ... --exit-code 1`. Consequence: the actual findings are never parsed into the manifest or findings.json, and the user is told the report is stale instead of seeing what was found. The non-adopted path parses the findings and then marks the suite failed.

Suggested correction: Check freshness (written within the step's start and completion window) separately from step success. When the report is fresh, parse it, record the findings, and then mark the suite failed using the step's failure kind, as the non-adopted path does. Add an adopted-mode test with a scanner that exits 1 and has findings.

### Pass 2 / TASK-REV-005 — minor: packaging/recipe-selection

Location: src/Cis.Host/Cis.Host.csproj:71-74; src/Cis.Modules.Repository/RepositoryExample.cs:20-23; examples/dotnet-engineering/.gitignore:1-5

The bundled recipe is whatever exists under examples/dotnet-engineering in the build working tree, minus five excluded patterns. It is not the 60 tracked files in the inventory. Default local outputs are neither excluded nor ignored: tests/*/TestResults from `dotnet test` without --results-directory, BenchmarkDotNet.Artifacts from a run without --artifacts, and Visual Studio's .vs or *.user files. Trigger: packaging from a working tree where the example was opened or run. Consequence: untracked local output, possibly traces with synthetic auth headers, is shipped and exported to users. Alternatively, the 200-file or 1 MiB check makes `cis repo example` fail with 'exceeds its file limits'.

Suggested correction: Select bundled files from an explicit tracked list, or align the Content exclusions with an expanded .gitignore (TestResults/, BenchmarkDotNet.Artifacts/, .vs/, *.user). Add a packaging test that the bundled example set equals the reviewed inventory.

### Pass 2 / TASK-REV-006 — minor: native-harness-preservation

Location: src/Cis.Modules.Repository/DotNetTestConfiguration.cs:68-73; src/Cis.Modules.Repository/RepositoryTestingStarter.cs:71 and :78

Microsoft.Testing.Platform is detected only from the TUnit or Microsoft.Testing.Platform packages or the MSTest.Sdk SDK. The detector does not read the global.json `test.runner` setting, TestingPlatformDotnetTestSupport, UseMicrosoftTestingPlatformRunner (used by the example at tests/unit/Example.UnitTests.csproj:5) or the NUnit/MSTest runner properties. Trigger: a repository that switches `dotnet test` to Microsoft.Testing.Platform mode through the SDK. Consequence: the starter emits a VSTest command (`--logger trx;LogFileName=...`) that does not apply to that runner, which the code comment says it intends to avoid. Projects that are detected as Microsoft.Testing.Platform are dropped from the profile with no warning, so the unit gate later appears missing without an explanation.

Suggested correction: Read these runner signals from global.json and the literal project/props settings. When the runner is ambiguous or is Microsoft.Testing.Platform, add a warning or an explicitly unverified placeholder suite instead of silently skipping it or emitting VSTest arguments. Add classification tests for xUnit v3 under the global.json Microsoft.Testing.Platform runner setting and for MSTest.Sdk.

### Pass 2 / TASK-REV-007 — observation: classification/regression

Location: src/Cis.Modules.Repository/DotNetTestConfiguration.cs:46-50 and :53-59; RepositoryClassifier.cs:189-224 (replaces the earlier text match shown at tracked-changes.diff:3888-3896)

Test detection moved from a text match (Microsoft.NET.Test.Sdk, IsTestProject, xunit, NUnit) to literal unconditional declarations only. PackageReference Update entries are also no longer collected. A test project whose test packages are all inside a conditioned ItemGroup and that does not set IsTestProject literally is no longer a test component. It goes through the web, worker or library branches; xUnit v3 test projects are also OutputType Exe. A warning is emitted, so this is not silent, but the component can receive production-oriented standards and skill packs.

Suggested correction: When conditional test or package declarations are found, classify the project as 'test-automation (unverified)' instead of falling through to the production branches, or keep the earlier text signal as a fallback. Add a classifier test for a conditionally referenced test project.

### Pass 2 / TASK-REV-008 — observation: known-gaps/review-scope

Location: first-review-result.json:2129-2207; review scope of this pass

Previously identified, not new: first-review TASK-REV-001 (editing requireTaskCompletion to false silently opts out of completion checks) and TASK-REV-002 (the implementer run ID is never verified) remain open, along with its five minor findings. Known qualification gaps also remain open: second executable skill integration and full packaged phased closure. Not reviewed in this pass: the example's OpenAPI and browser files (ReadingOpenApi, OpenApiCompatibilityTests, BrowserAcceptanceTests, ReadingPage.html, WebFixture, SyntheticIdentity); the content of EngineeringDefaultStandards.cs, the new skill and instruction files and the iteration plan specs; the BRD FeatureIntakeService story workflow diff; the PlanModule, PlanningService, Testing and Workflow diffs; most new tests. No checks were run.

Suggested correction: Resolve the two previously identified major findings and findings 1-2 above, each with negative tests. Arrange a focused pass over the unreviewed OpenAPI, browser and standards/skills areas, and finish the two known qualification gaps before any readiness decision.

## Evidence retention and next action

Raw provider results and events are retained locally under .cis/local/qualification/user-requested-claude-review*; the reviewed snapshot and its hash inventory are under .cis/local/qualification/reviewer-fixture/user-requested-review/. Finding IDs repeat across runs, so always retain the pass number. The first pass missing-file observation is corrected in the second snapshot; other findings have not been resolved. Triage the four major findings against the intended adoption and implementation-authority contracts, then add meaningful reproductions and fixes before requesting re-review. Complete the still-omitted areas and qualification scenarios before a readiness verdict.


## Corrective review — RUN-20261006045128-70C54029CF

Read-only check of the fixes in review-fixes against all 13 findings from planning/claude-engineering-defaults-review-2026-10-06.md, plus the observations. No commands or tests were run and no files were changed. The reported results (Release build passed; Repository 233 and Security 34 tests passed; Delivery had ten fixture/data failures that were corrected, with final regression still pending) were not reproduced.

Outcome per finding:
- Pass 1 / 001 (opt-out edit): partly resolved. Turning requireTaskCompletion off in a managed policy now blocks, but there is still no governed opt-out record, and two edits still turn it off silently.
- Pass 1 / 002 (implementer run never checked): resolved for plan tasks and for labelled human records. The story implementation path is still broken.
- Pass 1 / 003 (inapplicable gates): resolved for build, unit and coverage. This created a new deadlock for tasks that change no production code.
- Pass 1 / 004 (fixed 95/80 thresholds), 005 (one bad artifact hid later gates), 006 (one generic error message) and 007 (duplicate participant result): resolved.
- Pass 1 / 008 (missing .gitignore): resolved.
- Pass 1 / 009 (unsigned evidence manifests): not re-checked.
- Pass 1 / 010 and Pass 2 / 008 (known qualification gaps): still open, as you stated.
- Pass 2 / 001 (implicit adoption), 002 (SARIF severity), 003 (review unchanged check), 004 (failed scanner parsing) and 007 (conditional test projects): resolved.
- Pass 2 / 005 (example packaging): resolved. There is no test that the packaged list matches the reviewed inventory.
- Pass 2 / 006 (MTP runner detection): resolved in source. Its tests cover only TUnit.

Recommendation: revise.

### TASK-REV-001 — major

src/Cis.Modules.Plan/StoryEngineeringCompletion.cs:48-50; src/Cis.Modules.Plan/EngineeringCompletionReview.cs:61; src/Cis.Modules.Plan/EngineeringImplementationReview.cs:20,37-39; src/Cis.Modules.Agent/AgentService.StoryTasks.cs:290,304; src/Cis.Modules.Agent/AgentService.cs:2502

Trigger: closing a story task whose participant was implemented by a native agent run. The run manifest has taskId '<task>-IMPLEMENT' (StoryTasks.cs:290) and is stored under the authority repository's .cis/local/agents/runs (RunPath uses context.RepositoryPath). Story completion passes the participant repository path and the unsuffixed work.Task.Id, and EngineeringCompletionReview.cs:61 forwards task.Id rather than an implementation task ID. The review side gets a suffixed reviewTaskId; the implementation side does not. Consequence: every native story implementation fails with 'does not identify the completed current task contract' or the manifest-path error. The only way to close is a 'human-implemented' record, which docs/manual/cis_plan_task_completion_context.md:42 forbids for agent work. No test covers this path; the story tests use human records only (StoryEngineeringCompletionTests.cs:89).

Suggested correction: Pass an implementation task ID (work.Task.Id + "-IMPLEMENT") into EngineeringCompletionReview and EngineeringImplementationReview, as is already done for reviewTaskId. Resolve the native manifest relative to the authority repository that recorded the run, keeping the path-safety and hash checks. Add a story test that closes a participant from an AgentService-shaped implement manifest and result.

### TASK-REV-002 — major

src/Cis.Modules.Repository/EngineeringAssessmentService.cs:38; src/Cis.Modules.Plan/EngineeringTestEvidence.cs:28-29; src/Cis.Modules.Testing/ChangedCoverageProjection.cs:30

Trigger: an implementation-category task in an adopted repository that changes only tests, configuration or other non-production files. Coverage now cannot be marked inapplicable. With no changed production lines, ChangedCoverageProjection returns lines=0 and total=0, and SuitePasses requires lines at or above the threshold and measuredLines > 0. Consequence: such tasks can never complete and there is no governed route around it. The earlier correction asked for the unit/coverage pair to be non-waivable only when changed production lines exist.

Suggested correction: Allow coverage to be inapplicable only when a bound changed-production scope artifact shows zero changed production lines, or accept a coverage suite with measuredLines=0 and scope changed-production as passing. Keep build and unit non-waivable. Add a negative test (inapplicable with changed lines is rejected) and a positive test (zero changed lines can complete).

### TASK-REV-003 — minor

src/Cis.Abstractions/CisEngineeringPolicy.cs:32-36,53-64; src/Cis.Modules.Repository/EngineeringAlignmentDoctorCheck.cs:13; src/Cis.Modules.Repository/RepositoryInitializer.cs:249-257

The one-line opt-out edit is now blocked. Whether the policy was ever adopted is still read only from .cis/starter-manifest.yml, which the implementer can edit. Trigger: remove the .cis/engineering-defaults.json entry from managed_artifacts and set requireTaskCompletion=false. Consequence: assessment returns Adopted=false with no diagnostics, the alignment check returns [] for non-adopted repositories (:13), and a later repo init treats the repository as never adopted without warning, because the policy file exists. Teams that really need to withdraw also have no governed, traceable opt-out record; the original correction asked for both.

Suggested correction: Add an explicit opt-out record that is checked at assessment time (for example an approved decision reference with actor and date), and treat a managed-entry removal without that record as a blocking downgrade. Have the alignment check report a policy that was previously adopted but is now off. Add a negative test for the two-edit case.

### TASK-REV-004 — minor

tests/Cis.Modules.Delivery.Tests/EngineeringCompletionTests.cs:16-49; src/Cis.Modules.Agent/AgentService.cs:228,1047-1091

The only positive native-implementation test uses a hand-written manifest with provider 'fixture' and a fixed date. Nothing checks that an AgentService-produced implement manifest has the fields and formats the verifier reads: TaskContractDigest, OutputDigest, CompletedAtUtc format, an unprefixed ResultDigest, and EnvelopeId. If no task-contract reader is registered (AgentService.cs:228), implement runs record TaskContractDigest=null and are rejected only at completion. Review runs are blocked up front in that case (:249) but implement runs are not. Consequence: the native path could drift from the verifier, as TASK-REV-001 already shows for stories, without any test failing.

Suggested correction: Add an integration test that runs a fake provider through AgentService implement mode in an adopted repository and feeds its retained manifest and result to EngineeringCompletionReview. Block adopted implement runs when the task contract digest is unavailable, as review runs already are.

### TASK-REV-005 — minor

src/Cis.Host/ExampleRecipe.props:3-64; tests/Cis.Modules.Repository.Tests/RepositoryExampleTests.cs

The recipe is now an explicit 60-file list, which fixes the wildcard exposure. It is maintained by hand, though, and no test compares it with the reviewed example inventory or the files on disk. Trigger: adding or renaming an example file. Consequence: the packaged example silently drops or misses files, and the export still succeeds.

Suggested correction: Add a test that compares the Content items in ExampleRecipe.props with the tracked files under examples/dotnet-engineering, with explicit exclusions, so drift fails the build.

### TASK-REV-006 — minor

src/Cis.Modules.Repository/DotNetRunnerSettings.cs:10-33; tests/Cis.Modules.Repository.Tests/EngineeringAdoptionTests.cs:140-149

The source now reads global.json test.runner and the MTP runner properties, and warns instead of generating VSTest arguments. The only test covers TUnit. Neither the global.json runner setting, MSTest.Sdk, UseMicrosoftTestingPlatformRunner, nor the malformed-global.json warning path has a test, although the previous correction asked for xUnit v3 under global.json MTP and for MSTest.Sdk. Consequence: regressions in the new detector would go unnoticed.

Suggested correction: Add classifier and starter tests for xUnit v3 with global.json test.runner=Microsoft.Testing.Platform, for MSTest.Sdk, for UseMicrosoftTestingPlatformRunner=true, and for malformed global.json, asserting the warning and that no --logger trx is emitted.

### TASK-REV-007 — observation

src/Cis.Modules.Plan/EngineeringCompletionReview.cs:85-105; src/Cis.Modules.Repository/EngineeringAssessmentService.cs:38

The other 15 implementation gates (mutation, security, integration, business, browser and so on) can still be marked Inapplicable with only a rationale of 12 or more characters and a hash-matched artifact whose content is never checked. The alternative correction, that the bound reviewer result explicitly lists and accepts each gate marked inapplicable, was not implemented. This is narrower than before and depends on the independent review.

Suggested correction: Require the bound review result to list the gates it accepts as inapplicable, and check that list against the gates marked Inapplicable in ReviewResultPasses or EngineeringReviewEvidence.

### TASK-REV-008 — observation

review-fixes snapshot; planning/engineering-defaults-candidate-handoff.md

Reviewed: the sources and tests cited in evidence. Not reviewed: example OpenAPI and browser composition, EngineeringDefaultStandards.cs, consistency between skill and instruction guidance and contracts, internals of EngineeringReviewEvidence, EngineeringSecurityEvidence and BenchmarkEvidenceReview, Pass 1 / 009 trust-boundary documentation, lines 964–5575 of source-manifest.json, tracked-changes.diff, and most test assertions. No checks were run. The known gaps remain open separately: second executable agent-skill integration and full packaged phased closure. Final Delivery regression is still pending, as you stated.

Suggested correction: Fix TASK-REV-001 and TASK-REV-002 with tests, complete the final regression, and arrange a focused pass over the areas listed as not reviewed. Close the two known qualification gaps before any readiness decision.




## Follow-up review — RUN-20261006050637-A36270E5FF

I did a read-only review of review-fixes-final, limited to source, within the time budget. I didn't edit files, run commands, reproduce tests or grant approval. Correction 1 (story implementation provenance) is verified in source. Correction 2 (coverage inapplicability) is verified as specified, but it leaves one major defect. A change whose changed lines include no executable code (for example, only comments or blank lines) inside an instrumented production file reports changedProductionLines > 0 and measuredLines = 0. That fails both the pass check and the inapplicability check, so the coverage gate can never complete. This contradicts the starter guidance that zero executable changed lines requires scoped inapplicability. I only partly source-checked corrections 3, 4 and 5 against the stated claims. The full regression is still running and I did not reproduce it. Known separate qualification gaps (a second executable agent-skill integration and full packaged phased closure) remain open and are not counted as new defects. Recommendation: revise.

### TASK-REV-001 — major

review-fixes-final/src/Cis.Modules.Plan/EngineeringTestEvidence.cs:28-30; review-fixes-final/src/Cis.Modules.Testing/ChangedProductionScope.cs:45-54; review-fixes-final/src/Cis.Modules.Testing/ChangedCoverageProjection.cs:24-31

ChangedProductionLines counts every line Git reports as changed, including comments, blank lines, using directives and attributes. MeasuredLines counts only changed lines that have Cobertura entries. Suppose a change touches only non-executable lines in a production file that Cobertura already reports. The result is changedProductionLines > 0 and measuredLines = 0. The pass branch needs measuredLines > 0 and the inapplicability branch needs changedProductionLines == 0, so neither can succeed. A required coverage gate on such a task can never be completed. This contradicts EngineeringDefaultsStarter.cs:95 ('zero executable changed lines requires scoped inapplicability'). Source inspection only; not reproduced.

Suggested correction: Base the inapplicability decision on executable lines. For example, allow inapplicability when measuredLines == 0 and every changed production file appears in the Cobertura report, so uninstrumented new files still count as uncovered. Alternatively, emit a separate changedExecutableLines count and require it to be zero. Add positive and negative EngineeringSuiteEvidenceTests or ChangedCoverageTests cases for a comment-only change to an instrumented file, and keep the existing case where an uninstrumented new file blocks completion.

### TASK-REV-002 — observation

review-fixes-final (corrections 3, 4 and 5)

Within the four-minute budget I only partly verified corrections 3 (I confirmed the AgentRunManifest fields; I did not read the end-to-end AgentService test or the missing-task-contract-reader block), 4 (engineering-adoption.json detection and the documented limits on local governance tampering) and 5 (ExampleRecipeMatchesReviewableGitInventory, the 60-file/zero-leak candidate package probe and the EngineeringDiscoveryTests cases). These are unverified, not passed. The full regression result was not available to me.

Suggested correction: Treat corrections 3, 4 and 5 as claims backed by the implementer's evidence and the pending full regression, not by this review. Confirm the regression result before relying on them.

### TASK-REV-003 — observation

review-fixes-final/tests/Cis.Modules.Delivery.Tests/StoryEngineeringCompletionTests.cs:86-118

The native-agent path in the story test uses a hand-written manifest JSON, not one produced by AgentService. I didn't see a negative case in this test where a native run targets the wrong participant or uses the wrong task ID, although the validator handles both (EngineeringImplementationReview.cs:25, 43-44). The claimed AgentService integration test may cover produced-manifest compatibility, but I didn't inspect it.

Suggested correction: Add story-level negative cases: a native manifest whose targetRepositoryPath is the other participant, one whose taskId is T1 instead of T1-IMPLEMENT, and a native record placed in the participant repository instead of the authority repository. Each should produce the expected participant error.




## Follow-up review — RUN-20261006080141-B66ABBFE93

Read-only review of the coverage correction (WORK-092). I read only the source in the six supplied files and in the previous review. I ran nothing and edited nothing, and I don't treat any test as executed. The source does fix the comment-only coverage deadlock. Coverage can now be marked inapplicable when changedProductionLines is 0 or more, uninstrumentedChangedFiles is 0, measuredLines is 0, the base revision is full length, the scope is changed-production, and both native artifacts are bound (EngineeringTestEvidence.cs:28-34). Native artifacts and the current input identity are still required. The projection fills in UninstrumentedChangedFiles (ChangedCoverageProjection.cs:16-34). The tests include a positive comment-only case and a negative case for missing instrumentation. The story test now has wrong-target, wrong-task and wrong-location cases with receipt hashes kept current. One gap remains. Correctness now depends on what Number() returns when a field is absent, and that helper (EngineeringEvidenceJson) is outside the supplied files. No test covers a manifest that omits uninstrumentedChangedFiles or changedProductionLines. If an absent field reads as 0, an older or hand-written manifest without uninstrumentedChangedFiles could still be accepted as inapplicable. Recommendation: revise. Readiness is not certified. The complete native regression result was not available to me.

### TASK-REV-001 — major

coverage-correction/src/Cis.Modules.Plan/EngineeringTestEvidence.cs:29; coverage-correction/tests/Cis.Modules.Delivery.Tests/EngineeringSuiteEvidenceTests.cs:103-106,123-129

The inapplicability gate checks Number(coverage, "changedProductionLines") >= 0 and Number(coverage, "uninstrumentedChangedFiles") == 0. Both checks depend on what Number() returns for an absent or null property. Number() is in EngineeringEvidenceJson, which isn't among the supplied files, so I couldn't verify that behavior. Every inapplicable test row writes both fields (lines 126-127), so no test shows that a missing field is rejected. If an absent field reads as 0, a manifest without uninstrumentedChangedFiles would be accepted as inapplicable. That would bring back the uninstrumented-file gap the correction was meant to close. TestCoverageSummary.UninstrumentedChangedFiles is also nullable with a default of null (CisTestingContracts.cs:50), so a writer other than ChangedCoverageProjection could leave it out. The task's claim that ChangedProductionLines 'must be present' is likewise unproven within the supplied scope.

Suggested correction: Confirm that Number() returns a negative sentinel or fails for absent or null properties. If it doesn't, require both properties to exist with JSON number type before applying the inapplicable predicate. Add MeasurementMustBelongToPassingSuite rows that omit uninstrumentedChangedFiles, omit changedProductionLines, and set uninstrumentedChangedFiles to null. Each should be rejected.

### TASK-REV-002 — minor

coverage-correction/tests/Cis.Modules.Delivery.Tests/StoryEngineeringCompletionTests.cs:136-145,148-152

The wrong-location scenario copies the manifest to second.Path/.cis/local/agents/runs/implementation-second/manifest.json (line 138). The restore at lines 144-145 doesn't delete that copy. The later stale-participant assertion (lines 149-152) then runs with the leftover file still in the second repository. If .cis/local content is part of the participant's input identity, participant 2 could already be stale before Changed.cs is written, and the stale assertion would pass for the wrong reason. The supplied files don't show whether .cis/local is excluded. Also, the rejection assertion (line 143) only checks for the substring 'implementation'. It doesn't tell apart the three causes it is meant to cover: target mismatch, task-ID mismatch and wrong location.

Suggested correction: Delete the participant-local copy as part of each scenario's restore. Assert the specific participant error expected for each scenario, for example the target, task-ID or authority-location message from the validator.

### TASK-REV-003 — observation

coverage-correction/tests/Cis.Modules.Testing.Tests/ChangedCoverageTests.cs:46-55; coverage-correction/tests/Cis.Modules.Delivery.Tests/EngineeringSuiteEvidenceTests.cs:118-129

The comment-only case is tested in two separate halves. The projection test checks the TestCoverageSummary values. The gate test uses hand-written manifest JSON with camelCase names. No supplied test feeds a projection-produced summary through manifest serialization into EngineeringTestEvidence, so the serialized property names (for example uninstrumentedChangedFiles) are assumed to match rather than shown to. If they differ, the problem described in TASK-REV-001 could apply to real manifests.

Suggested correction: Optionally add one end-to-end case. Serialize the comment-only projection result through the native manifest writer and check that the coverage gate accepts it as Inapplicable, and that the uninstrumented-file variant is rejected.

### TASK-REV-004 — observation

Native verification evidence for WORK-092

The build result (zero warnings/errors) is the implementer's claim. The complete native regression was still running and no result was supplied. I didn't run anything, and I don't infer test execution from source.

Suggested correction: Get the complete native regression result and the native artifacts before relying on this correction. A missing or stale regression result is not a pass.




## Final focused correction review — RUN-20261006080539-BA54C9D174

Read-only review of the WORK-092 coverage correction. I read last-review.json and the supplemental source: EngineeringEvidenceJson, EngineeringTestEvidence, CisExecutionIdentity, EngineeringImplementationReview, StoryEngineeringCompletion, ChangedCoverageProjection, CisTestingContracts and the three test files. I found no reproducible source defect. All three earlier findings that needed correction are addressed in source. (1) EngineeringEvidenceJson.Number returns -1 when a value is absent, null, non-numeric or non-finite. The inapplicable coverage gate therefore rejects a missing or null uninstrumentedChangedFiles (-1 != 0) and a missing changedProductionLines (-1 < 0). New rows in MeasurementMustBelongToPassingSuite (lines 107-109) change only that one field against the row-103 baseline, which passes, and expect rejection. (2) The story negative cases now each assert a specific diagnostic, and each one matches a real validator message. The wrong-location restore deletes the participant-local copy and re-checks that every context is clean before the later contract-change and stale-input assertions. CisExecutionIdentity also skips .cis/local, so the leftover copy could not have affected those assertions in any case. (3) ChangedCoverageTests now checks the camelCase names of a serialized TestCoverageSummary, including uninstrumentedChangedFiles. It uses JsonSerializerDefaults.Web rather than the native manifest writer, which is noted below as an observation only. I ran no commands, builds or tests. The full native regression is still running; I have not reproduced it and do not claim its result. This review is advisory, does not approve anything, and does not certify trial readiness.

Only observations and optional test additions remain in this scoped review. It does not certify the entire candidate or the new-project trial.

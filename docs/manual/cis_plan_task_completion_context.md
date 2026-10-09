---
title: "cis plan task completion-context"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-10-05"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-plan-task-completion-context
---

# `cis plan task completion-context`

Print a JSON template identifying the current task, source inputs, graph, adopted engineering policy and required gate inventory. Every gate starts **Missing**. This read-only command does not run checks, write a receipt, approve a finding or complete a task.

```text
cis plan task completion-context <change-id> <task-id> [--repo <authority-path>] [--target <repository-id>] [--format human|json|agent]
```

Adopted engineering defaults and the graph module are required. JSON is the default and contains the full template; human and agent formats summarize the missing gate inventory. Diagnostics go to stderr. Exit 0 means context was produced, not that the task is ready. Exit 2 means required context is unavailable or invalid.

The required inventory comes from current classification, task scope and additional adopted policy gates, independently of configured commands. Unknown implementation categories receive the conservative implementation inventory. Documentation/design/coordination scopes retain graph, alignment, context, traceability and independent-review obligations without automatically inheriting every code-test layer.

Retain the completed receipt at the returned `receiptPath`, relative to the task authority. Authority/standalone tasks keep `verification/<task-id>.json` inside the existing change dossier. Participant tasks use `verification/<task-id>/<repository-id>.json`. Fill `gates` with states, concrete rationale and artifact paths plus `sha256:` hashes. Identify distinct implementation and reviewer runs and unresolved blocking findings. Preserve the exact task requirement IDs. Never fill a passed state from the template or from a command definition alone.

For a task with several declared targets, request one template per target using `--target`; a single target is selected automatically. This selects a template, not a smaller completion scope. The existing transition checks every declared owned target. After adoption is established, missing, ambiguous, unregistered or dependency-only targets block the assessment. When the authority or any declared target has adopted completion policy, every declared target must have reviewed adoption before the combined task can complete. CIS does not adopt it automatically. With a valid workspace and known adoption state, projects with no adoption retain their existing lifecycle; standalone legacy component targets remain supported. When a product workspace exists, CIS first needs a valid registry and a registered, resolvable context for every declared owned target. An invalid workspace, unavailable registry, unregistered target or unresolved owned context blocks completion because adoption cannot be determined, even if the authority has not adopted.

Each template identifies its `repositoryId`. Its input identity, policy, graph and required gates belong to that repository. Ordinary gate artifacts and explicitly labelled human implementation records are relative to that target. Native implementation records and the review gate's manifest/result remain relative to the authority, at their original `.cis/local/agents/runs/<run-id>/` locations. Do not copy them into the participant to satisfy path checks. Native runs must identify the intended participant and separate provider sessions. Participant reviews bind `authorityInputDigest` for authority requirements, standards and configuration, and `authorityDossierDigest` for proposal, impact, design, decisions, test cases and verification content. Adding, removing or changing those dossier documents requires a fresh review. The dossier identity excludes only generated tool-usage rows in the verification ledger. The selected plan row and task document remain bound separately by the task contract. Older participant review records without these bindings cannot pass this contract. Native implementation provenance binds the completed task and final participant output; the later review establishes current authority context.

The dossier binding applies to participant reviews and covers the whole change, including those six documents shared by other tasks. Editing a shared decision or test case invalidates every participant review in that change. Authority-only reviews retain their existing source/task binding; they do not acquire this participant dossier binding. Each dossier document must be valid UTF-8 and at most 256 KiB; their combined size must be at most 1 MiB. If those limits block execution or completion, narrow or split the change scope while preserving the required source context, then prepare a fresh review. Only verification rows matching the exact generated tool-usage format are excluded; arbitrary snapshot-shaped prose remains bound.

Native test gates require successful current CIS test manifests containing nonzero, non-skipped tests at the required layer. Coverage requires changed-production scope and at least 95% line coverage; release mutation requires at least 80%. Preserve stronger adopted policy in the underlying native configuration. Build, lint and format gates require successful workflow steps named `build`, `lint` and `format`. Review requires a successful read-only provider run for this task and its matching ready review result, without blocking findings. Existing provider selection remains authoritative.

Semantic gates need source-backed assessment and integrity-checked artifacts. Artifact integrity is not independent proof of the truth of every claim; the assigned reviewer must inspect the evidence. The first contract does not replace human authority, live provider qualification or every standard's native enforcement.

An artifact can provide `selector` to bind an existing workflow step with a different name, such as the project's adopted lint job. Otherwise the gate ID is used. Native test manifests must retain unchanged underlying result/artifact hashes and sizes; hashing the manifest alone does not protect a subsequently modified TRX or diagnostic artifact.

Execution input identity includes repository files and uncommitted additions. Known generated directories, `.cis/local/`, Git metadata and the current documentation root's change dossiers are excluded. Task identity covers both the plan row and task document, excluding the lifecycle status and generated tool-usage snapshot. Inputs are checked again after evidence review to detect intervening edits. Review actual scope and omitted/generated files; a fresh graph or digest alone does not prove complete context.

For changed C# coverage, retain `.cis/coverage-scope.json` with `schemaVersion: 1`, an exact `baseRevision` commit and reviewed `productionPaths`. The Cobertura adapter measures executable changed lines and counts missing-file coverage against the result. Zero measured lines cannot pass. This scope is independently reviewed; CIS cannot infer from a small selected directory that every required production path was included.

The [task transition](cis_plan_task_transition.md) rejects failed, missing, stale and skipped required gates. A justified inapplicable gate remains distinct and cannot replace an unconditional obligation. Edits to relevant inputs or adopted policy require fresh evidence.

## Native review identity

The receipt's `implementation` field contains a repository-relative `path` and `sha256` hash. For agent work, point it at `.cis/local/agents/runs/<implementerRunId>/manifest.json` and retain its sibling `result.json`. Completion checks a successful implementation run, matching native result, task contract, final `outputDigest`, provider and provider session. The review must begin after implementation completes and must use a separate run and provider session. Files changed during a read-only review invalidate the run, including files that were already modified before it started.

Human work uses an explicitly labelled record with `schemaVersion: 1`, `kind: "human-implemented"`, `runId` equal to `implementerRunId`, a named `actor`, current `taskId`, `taskContractDigest`, final `inputDigest`, and an ISO-8601 `completedAtUtc`. This records authorship for review; it is not an agent run or a governance approval. Never label agent implementation as human work.

The adopted policy can set `minimumCoverageLines` from 95 to 100 and `minimumMutationScore` from 80 to 100. Completion applies these thresholds as well as native suite outcomes. Implementation build and unit gates cannot be marked inapplicable. Coverage inapplicability requires a current passing native test manifest with changed-production scope, a retained base revision, zero measured executable changed lines and zero uninstrumented changed files, bound to its own coverage report. This permits comment-only edits in instrumented files while missing instrumentation still blocks. Artifact failures are reported per gate so later missing checks remain visible.

Coverage and mutation thresholds above are minimum completion defaults; stronger project thresholds remain authoritative. A coverage summary must reference its passing test suite's own coverage artifact. Mutation must come from a mutation suite and its own native report. Every suite artifact must match an integrity-checked manifest record.

Native security manifests may pass with findings below their adopted blocking severities or with exact, approved, unexpired acceptances. Reconciliation retains the severity policy and acceptance metadata. Completion rechecks expiry, scanner/fingerprint identity and each suite's scanner report. Missing acceptance provenance or an expired exception blocks completion.

An adopted `agent run --mode review --permission read-only` captures the plan-owned task contract and requires structured task findings. Its native result has schema version 2 and status `Succeeded`. Completion verifies that result against its manifest, source identity and current task contract. Regenerating a receipt after changing acceptance criteria does not refresh the review. Resuming a run preserves its original identity; begin a fresh review after material changes.

## Performance baseline

In Git repositories, recognized output-directory names are excluded only when they contain no tracked or nonignored input. Source beneath a directory such as `src/Artifacts` remains fingerprinted. Before Git adoption, only project-adjacent native `bin`/`obj` and package-adjacent `node_modules` receive that treatment; ambiguous output names remain inputs. Git classification failure blocks freshness instead of silently omitting files.

An applicable performance gate requires `.cis/performance-policy.json` with schema version 1 and these fields:

- `baseline`: the immutable native BenchmarkDotNet full JSON report's repository-relative `path` and `sha256:` hash.
- `workload`, `resourceLimits`, `baselineDecision`: concrete descriptions of the representative scope, controlled conditions and reviewed baseline selection.
- `minimumSamples` (at least 3), `maximumRegressionPercent` (0–100), and positive `maximumStandardErrorPercent`.
- `benchmarks`: unique native `fullName` identities with `maximumMeanNanoseconds` and `maximumAllocatedBytes` budgets.

The gate artifacts must include exactly one native report with selector `benchmark-candidate` and a current successful workflow state with selector `performance`. The workflow must contain a successful step named `performance`; the candidate report must have been written during that step. Native host/tool/runtime configuration and benchmark job/workload/instrumentation parameters must match the baseline. Missing, duplicate, incompatible, incomplete, noisy or over-budget measurements block completion. CIS does not adopt a baseline or increase a budget automatically. The assigned reviewer must verify the declared resource conditions and that the selected benchmark scope covers the actual performance obligation.

For story tasks, CIS reads native implementation records from the registered authority repository, where the agent service retained them. The implementation task ID is `<task-id>-IMPLEMENT`; the review ID is `<task-id>-REVIEW`. The native manifest must target the participant being closed. Human records remain relative to the participant and use the unsuffixed task ID. Do not copy or relabel agent evidence as human authorship.

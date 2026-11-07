---
name: cis-dotnet-test
description: Build and test affected .NET projects with bounded commands and preserved evidence. Use when changing C# or .NET code.
---

# CIS Dotnet Test

## Purpose

Verify .NET changes without turning a targeted edit into an unbounded solution-wide run.

## Workflow

1. Identify affected projects, adopted framework, native runner/platform and direct test projects; inspect inherited configuration. When no harness is adopted, consult `.cis/local/examples/dotnet-engineering/README.md` when installed and the engineering-defaults reference. Verify compatibility before adoption; examples are reference material, not project implementation or passing evidence.
2. Run the native formatter and SDK/Sonar analyzers or adopted alternatives with declared blocking severities.
3. Build the narrowest project boundary with warnings visible.
   Demonstrate ordinary native discovery and test execution on the supported
   installation; for applicable Windows/.NET work include normal Visual Studio
   Run All. Record an unavailable route and its completion implication explicitly.
4. Match execution to the checkpoint: ordinary edits use affected builds/focused native tests; coherent batches use affected regression and one review; documentation-only corrections use documentation/generation checks; mutation fixture/adapter changes require a small correctness pilot; feature candidates require full affected-module mutation and applicable checks; release candidates retain all required families/platforms/compatibility/package checks. State the behavioral outcome, relevant tests, time/storage budget and stop rule. Record justified inapplicability separately.
5. Enable correlated test-mode logs, metrics and traces without changing business/authentication behavior; retain partial evidence and first failures.
6. Record exact commands, versions, source identity, results, skipped checks, unavailable prerequisites and residual risk. Assess required gates independently of the configured command list before completion.

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

## Completion evidence

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

## Guardrails

Do not report a build as proof that behavior or architecture tests passed.

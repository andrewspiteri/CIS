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
4. Match execution to the checkpoint: ordinary edits use affected builds/focused native tests; coherent batches use affected regression and one review; documentation-only corrections use documentation/generation checks; mutation fixture/adapter changes require a small correctness pilot; feature candidates require full affected-module mutation and applicable checks; release candidates retain all required families/platforms/compatibility/package checks. State the behavioral outcome, relevant tests, time/storage budget and stop rule. Record justified inapplicability separately.
5. Enable correlated test-mode logs, metrics and traces without changing business/authentication behavior; retain partial evidence and first failures.
6. Record exact commands, versions, source identity, results, skipped checks, unavailable prerequisites and residual risk. Assess required gates independently of the configured command list before completion.

## Completion evidence

Preserve affected scope, exact commands, outcomes, generated artifacts, skipped checks, and residual risk in the canonical task or verification record.

## Guardrails

Do not report a build as proof that behavior or architecture tests passed.

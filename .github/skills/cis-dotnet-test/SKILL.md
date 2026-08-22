---
name: cis-dotnet-test
description: Build and test affected .NET projects with bounded commands and preserved evidence. Use when changing C# or .NET code.
---

# CIS Dotnet Test

## Purpose

Verify .NET changes without turning a targeted edit into an unbounded solution-wide run.

## Workflow

1. Identify the affected project and its direct test projects.
2. Run formatting or analyzers required by the repository.
3. Build the narrowest project boundary with warnings visible.
4. Run targeted tests, then widen only when impact or failures justify it.
5. Record exact commands, results, skipped checks, and residual risk.

## Completion evidence

Preserve affected scope, exact commands, outcomes, generated artifacts, skipped checks, and residual risk in the canonical task or verification record.

## Guardrails

Do not report a build as proof that behavior or architecture tests passed.

---
title: "Test Suite Profile"
type: test-suite-profile
status: Active
owner: "Repository maintainers"
last_reviewed: "2026-08-26"
review_cadence: "on test framework, command, classification, or CI-tier change"
---

# Test suite profile

Result, coverage, mutation, and retained artifact paths are derived state under `.cis/local/`.
Suite-specific sanitized runtime evidence uses
`.cis/local/testing/diagnostics/<suite-id>/`; CIS hashes and correlates those files during
`cis test reconcile`.

| Suite ID | Component | Layer | Framework | Command | Working directory | Result format | Result path | Coverage path | Mutation path | Prerequisites | Applies when | CI tier | Artifacts |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| dotnet-tests | repository | unit | dotnet-test | dotnet test ChangeImpactStudio.slnx --no-build --no-restore --logger trx;LogFileName=dotnet-tests.trx --results-directory .cis/local/testing/results | . | trx | .cis/local/testing/results/dotnet-tests.trx | - | - | .NET SDK | C# production changes | pr | retain-on-failure |

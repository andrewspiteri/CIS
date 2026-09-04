---
title: "Test Suite Profile"
type: test-suite-profile
status: Active
owner: "Repository maintainers"
last_reviewed: "2026-08-27"
review_cadence: "on test framework, command, classification, or CI-tier change"
---

# Test suite profile

Result, coverage, mutation, and retained artifact paths are derived state under `.cis/local/`.
Suite-specific sanitized runtime evidence uses
`.cis/local/testing/diagnostics/<suite-id>/<run-id>/attempt-<number>/`; CIS hashes and correlates those files during
`cis test reconcile`.

| Suite ID | Component | Layer | Framework | Command | Working directory | Result format | Result path | Coverage path | Mutation path | Prerequisites | Applies when | CI tier | Artifacts |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| dotnet-tests | repository | unit | dotnet-test | pwsh tools/run-dotnet-tests.ps1 | . | trx | .cis/local/testing/results/dotnet-tests.trx | .cis/local/testing/coverage/summary.json | - | .NET SDK and restored packages | C# production changes | pr | retain-on-failure |
| vscode-extension-tests | vscode-extension | frontend-component | node-test | pwsh tools/run-vscode-extension-tests.ps1 | . | junit | .cis/local/testing/results/vscode-extension-tests.junit.xml | .cis/local/testing/coverage/vscode-extension-summary.json | - | Node.js 24 or later and a built CIS CLI | VS Code extension, command, projection, trust, path, webview, packaging, or agent-workspace changes | pr | retain-on-failure |
| cis-safety-mutation | cis-abstractions | mutation | dotnet-stryker | pwsh tools/run-mutation.ps1 | . | stryker-json | .cis/local/testing/mutation/cis-abstractions.json | - | .cis/local/testing/mutation/cis-abstractions.json | .NET SDK and restored local tools | path, archive, process, import, or execution boundary changes | release | retain |

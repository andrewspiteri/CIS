---
title: "Test Suite Profile"
type: test-suite-profile
status: Active
owner: "Repository maintainers"
last_reviewed: "2026-11-24"
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
| cis-safety-mutation | cis-abstractions | mutation | dotnet-stryker | pwsh tools/run-mutation.ps1 | . | stryker-json | .cis/local/testing/mutation/cis-safety/latest-result.json | - | .cis/local/testing/mutation/cis-safety/latest-result.json | .NET SDK and restored local tools | path, archive, process, import, or execution boundary changes | release | retain |
| cis0005-regression | repository | regression | dotnet-test | dotnet exec --fx-version 10.0.12 "C:/Program Files/dotnet/sdk/10.0.401/vstest.console.dll" tests/Cis.Abstractions.Tests/bin/Release/net10.0/Cis.Abstractions.Tests.dll tests/Cis.Host.Tests/bin/Release/net10.0/Cis.Host.Tests.dll tests/Cis.Modules.Agent.Tests/bin/Release/net10.0/Cis.Modules.Agent.Tests.dll tests/Cis.Modules.Context.Tests/bin/Release/net10.0/Cis.Modules.Context.Tests.dll tests/Cis.Modules.Feedback.Tests/bin/Release/net10.0/Cis.Modules.Feedback.Tests.dll tests/Cis.Modules.Graph.Tests/bin/Release/net10.0/Cis.Modules.Graph.Tests.dll tests/Cis.Modules.TechnicalIntent.Tests/bin/Release/net10.0/Cis.Modules.TechnicalIntent.Tests.dll tests/Cis.Modules.Delivery.Tests/bin/Release/net10.0/Cis.Modules.Delivery.Tests.dll tests/Cis.Modules.Testing.Tests/bin/Release/net10.0/Cis.Modules.Testing.Tests.dll /Settings:tools/cis0005-closeout-coverage.runsettings /Logger:trx;LogFileName=regression.trx /ResultsDirectory:.cis/local/testing/current-context-closeout/central-stage1 /Diag:.cis/local/testing/current-context-closeout/central-stage1/vstest-diag.log | . | trx | .cis/local/testing/current-context-closeout/central-stage1/regression.trx | .cis/local/testing/current-context-closeout/central-stage1/*/coverage.cobertura.xml | - | Current matching owning builds and native collector | CIS-0005 changed production and completion contracts | focused | retain |
| cis0005-pure-unit | repository | unit | dotnet-test | dotnet exec --fx-version 10.0.12 "C:/Program Files/dotnet/sdk/10.0.401/vstest.console.dll" tests/Cis.Abstractions.Tests/bin/Release/net10.0/Cis.Abstractions.Tests.dll tests/Cis.Modules.Graph.Tests/bin/Release/net10.0/Cis.Modules.Graph.Tests.dll tests/Cis.Modules.Agent.Tests/bin/Release/net10.0/Cis.Modules.Agent.Tests.dll tests/Cis.Modules.Delivery.Tests/bin/Release/net10.0/Cis.Modules.Delivery.Tests.dll /Settings:tools/cis0005-closeout-unit.runsettings /Logger:trx;LogFileName=unit.trx /ResultsDirectory:.cis/local/testing/current-context-closeout/contracts-stage2/unit /Diag:.cis/local/testing/current-context-closeout/contracts-stage2/unit/vstest-diag.log | . | trx | .cis/local/testing/current-context-closeout/contracts-stage2/unit/unit.trx | - | - | Current owning builds; exact reviewed case selection | CIS-0005 unit contract checks | focused | retain |
| cis0005-integration | repository | integration | dotnet-test | dotnet exec --fx-version 10.0.12 "C:/Program Files/dotnet/sdk/10.0.401/vstest.console.dll" tests/Cis.Modules.Graph.Tests/bin/Release/net10.0/Cis.Modules.Graph.Tests.dll tests/Cis.Modules.Agent.Tests/bin/Release/net10.0/Cis.Modules.Agent.Tests.dll tests/Cis.Modules.Testing.Tests/bin/Release/net10.0/Cis.Modules.Testing.Tests.dll /Settings:tools/cis0005-closeout-integration.runsettings /Logger:trx;LogFileName=integration.trx /ResultsDirectory:.cis/local/testing/current-context-closeout/contracts-stage2/integration /Diag:.cis/local/testing/current-context-closeout/contracts-stage2/integration/vstest-diag.log | . | trx | .cis/local/testing/current-context-closeout/contracts-stage2/integration/integration.trx | - | - | Current owning builds; exact reviewed case selection | CIS-0005 integration contract checks | focused | retain |
| cis0005-api-compatibility | repository | api-compatibility | dotnet-test | dotnet exec --fx-version 10.0.12 "C:/Program Files/dotnet/sdk/10.0.401/vstest.console.dll" tests/Cis.Modules.Testing.Tests/bin/Release/net10.0/Cis.Modules.Testing.Tests.dll tests/Cis.Modules.Delivery.Tests/bin/Release/net10.0/Cis.Modules.Delivery.Tests.dll /Settings:tools/cis0005-closeout-api.runsettings /Logger:trx;LogFileName=api.trx /ResultsDirectory:.cis/local/testing/current-context-closeout/contracts-stage2/api /Diag:.cis/local/testing/current-context-closeout/contracts-stage2/api/vstest-diag.log | . | trx | .cis/local/testing/current-context-closeout/contracts-stage2/api/api.trx | - | - | Current owning builds; exact reviewed case selection | CIS-0005 api-compatibility contract checks | focused | retain |

## CIS-0005 selected scope

These additional suites measure only their declared current Windows x64 scopes. The central mixed suite is regression; the separate unit, integration and API suites each execute their own exact selection and native TRX. It does not
replace the original three suites, establish full-profile or release qualification, or
relax their thresholds. Native changed-production C# coverage uses the reviewed
.cis/coverage-scope.json and the unchanged 95 percent completion minimum. The
nonrecursive coverage selector chooses the actual GUID attachment and retains its
native TRX copy without counting it as a second report. Source completion requires
explicit selected-suite reconciliation and independent review of the actual coverage.

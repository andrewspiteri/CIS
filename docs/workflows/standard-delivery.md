---
title: "Standard Delivery Verification Workflow"
type: workflow-definition
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: "on build, test, runtime, or classification change"
cis:
  stable_id: change-impact-studio:workflow:standard-delivery
---

# Standard delivery verification workflow

CIS executes these arguments without a shell and checkpoints each step under
`.cis/local/`. This definition is specific to the CIS .NET solution.

| Step | Command | Working directory | Test suites | Depends on | Continue on failure | Timeout seconds |
|---|---|---|---|---|---|---:|
| build | dotnet build ChangeImpactStudio.slnx --no-restore | . | - | - | no | 1200 |
| dotnet-tests | dotnet test ChangeImpactStudio.slnx --no-build --no-restore --logger trx;LogFileName=dotnet-tests.trx --results-directory .cis/local/testing/results | . | dotnet-tests | build | no | 3600 |
| extension-syntax | node --check vscode-extension/extension.js | . | - | build | no | 300 |
| extension-tests | node --test vscode-extension/test/*.test.js | . | - | extension-syntax | no | 600 |
| docs | cis docs validate --root docs --strict | . | - | dotnet-tests | no | 600 |

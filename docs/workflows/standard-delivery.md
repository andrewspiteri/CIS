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
| build | dotnet build ChangeImpactStudio.slnx -c Release --no-restore | . | - | - | no | 1200 |
| dotnet-tests | pwsh tools/run-dotnet-tests.ps1 | . | dotnet-tests | build | no | 3600 |
| extension-syntax | node --check vscode-extension/extension.js | . | - | build | no | 300 |
| extension-tests | pwsh tools/run-vscode-extension-tests.ps1 | . | vscode-extension-tests | extension-syntax | no | 600 |
| cis-sast | node tools/run-security-scan.mjs sast | . | cis-sast | build | no | 1800 |
| cis-secrets | node tools/run-security-scan.mjs secrets | . | cis-secrets | build | no | 900 |
| cis-filesystem | node tools/run-security-scan.mjs filesystem | . | cis-filesystem | build | no | 1800 |
| docs | dotnet src/Cis.Host/bin/Release/net10.0/cis.dll docs validate --repo . --strict | . | - | dotnet-tests,cis-sast,cis-secrets,cis-filesystem | no | 600 |

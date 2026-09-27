---
title: "CIS release assurance workflow"
type: workflow-definition
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-27"
review_cadence: "on release test, coverage, mutation, or evidence change"
cis:
  stable_id: change-impact-studio:workflow:release-assurance
---

# Release assurance workflow

This workflow binds all applicable CIS test-suite profiles to one fresh release
manifest. Security scanners remain independently recorded by `standard-delivery` and
`security-verification`.

CI and tagged releases also run `tools/build-standalone.ps1` on Windows x64 and
Linux x64. Each job publishes a self-contained single-file executable, tests it with
`tools/test-standalone.ps1`, and retains an OS-specific archive and checksums. The
isolated smoke test covers module loading, embedded starters, SQLite graph storage
and C# runtime metadata binding. Both native jobs must pass before release packaging.
The release bundle includes both archives alongside the .NET tool, VS Code extension,
source archive and combined `SHA256SUMS`.

| Step | Command | Working directory | Test suites | Depends on | Continue on failure | Timeout seconds |
|---|---|---|---|---|---|---:|
| build | dotnet build ChangeImpactStudio.slnx -c Release --no-restore | . | - | - | no | 1200 |
| dotnet-tests | pwsh tools/run-dotnet-tests.ps1 | . | dotnet-tests | build | no | 3600 |
| extension-syntax | node --check vscode-extension/extension.js | . | - | build | no | 300 |
| extension-tests | pwsh tools/run-vscode-extension-tests.ps1 | . | vscode-extension-tests | extension-syntax | no | 600 |
| cis-safety-mutation | pwsh tools/run-mutation.ps1 | . | cis-safety-mutation | dotnet-tests,extension-tests | no | 3600 |
| docs | dotnet src/Cis.Host/bin/Release/net10.0/cis.dll docs validate --repo . --strict | . | - | cis-safety-mutation | no | 600 |

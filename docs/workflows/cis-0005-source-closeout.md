---
title: "CIS-0005 source closeout regression and coverage"
type: workflow-definition
status: Active
owner: "CIS maintainers"
last_reviewed: "2026-11-25"
review_cadence: "on CIS-0005 scope, command, collector or evidence change"
cis:
  stable_id: change-impact-studio:workflow:cis-0005-source-closeout
---

# CIS-0005 source closeout regression and coverage

This mixed Windows x64 regression selection contains real processes and persistence.
It is not a pure unit suite. Its native collector measures all changed C# production
under .cis/coverage-scope.json at the unchanged 95 percent threshold. The three
original profile rows and full/default reconciliation retain their original meaning.

The controller admits the unchanged physical ceiling/reserve and a 3,600-second
parent before launch: build180 plus regression3300 leaves120 seconds for overhead.
VSTest uses two independent assembly hosts; shared builds remain sequential. Native
parallel collection is qualified only if this actual session returns one readable
merged Cobertura attachment with intact native provenance. Do not manually merge,
rename or alter reports to obtain a pass. Preserve the deeper native TRX attachment
copy while the one-level selector counts the merged native file once.

Before this workflow, build all nine owners/helpers with the exact source version
below, run the separately bounded contracts workflow, and freeze both workflows,
profiles and runsettings. Concurrent graph work requires a separate combined physical admission; test fixtures use independent temporary repositories.
Use a new reviewed output path after any failed/previous attempt; never resume
over an original report. Launcher SDK10.0.401/runtime10.0.12 are pinned; probe
testhost10.0.15 is a separate observed child runtime, not a universal runtime claim.
No release, cross-platform or full-profile qualification follows from this scope.

| Step | Command | Working directory | Test suites | Depends on | Continue on failure | Timeout seconds |
| --- | --- | --- | --- | --- | --- | --- |
| build | dotnet exec --fx-version 10.0.12 "C:/Program Files/dotnet/sdk/10.0.401/dotnet.dll" build ChangeImpactStudio.slnx -c Release --no-restore --maxcpucount:1 -nr:false -p:UseSharedCompilation=false -p:IncludeSourceRevisionInInformationalVersion=false -p:InformationalVersion=0.3.0+worktree.94a78c097aa006fc479a5e32db8be3aeeadb9891f18fdd1ddb809492ca2f8732 | . | - | - | no | 180 |
| regression | dotnet exec --fx-version 10.0.12 "C:/Program Files/dotnet/sdk/10.0.401/vstest.console.dll" tests/Cis.Abstractions.Tests/bin/Release/net10.0/Cis.Abstractions.Tests.dll tests/Cis.Host.Tests/bin/Release/net10.0/Cis.Host.Tests.dll tests/Cis.Modules.Agent.Tests/bin/Release/net10.0/Cis.Modules.Agent.Tests.dll tests/Cis.Modules.Context.Tests/bin/Release/net10.0/Cis.Modules.Context.Tests.dll tests/Cis.Modules.Feedback.Tests/bin/Release/net10.0/Cis.Modules.Feedback.Tests.dll tests/Cis.Modules.Graph.Tests/bin/Release/net10.0/Cis.Modules.Graph.Tests.dll tests/Cis.Modules.TechnicalIntent.Tests/bin/Release/net10.0/Cis.Modules.TechnicalIntent.Tests.dll tests/Cis.Modules.Delivery.Tests/bin/Release/net10.0/Cis.Modules.Delivery.Tests.dll tests/Cis.Modules.Testing.Tests/bin/Release/net10.0/Cis.Modules.Testing.Tests.dll /Settings:tools/cis0005-closeout-coverage.runsettings /Logger:trx;LogFileName=regression.trx /ResultsDirectory:.cis/local/testing/current-context-closeout/central-stage1 /Diag:.cis/local/testing/current-context-closeout/central-stage1/vstest-diag.log | . | cis0005-regression | build | no | 3300 |

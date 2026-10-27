---
title: "cis test incremental"
type: command-reference
status: Draft
owner: "Andrew Spiteri"
last_reviewed: "2026-10-27"
review_cadence: on incremental execution contract change
cis:
  stable_id: change-impact-studio:manual:cis-test-incremental
---

# `cis test incremental`

Run pending unit tests and mutation work in bounded file batches. CIS saves each
completed result immediately and reuses it while its relevant inputs still match.
An interrupted file batch repeats that file; earlier completed files remain saved.
This command provides development feedback. It does not replace release qualification.

Mutation profiles designate a `canarySourceFile` with tests known to detect at
least `minimumCanaryKills` native mutants. That file runs first and its successful
result can be reused. A canary with no required kills stops further mutation work;
check the native adapter, mutant activation and assertions before continuing.
Unit-only profiles do not require a mutation canary.

```text
cis graph build --repo <repository> --format agent
cis test incremental plan --profile <relative.json> --repo <repository> --format agent
cis test incremental run --profile <relative.json> --repo <repository> --layer all --max-batches 4 --max-seconds 1200 --format agent
cis test incremental status --profile <relative.json> --repo <repository> --format agent
```

`plan` and `status` read the current graph, inputs and saved reports without running
tests. `run` executes pending batches. `--layer unit` runs unit work alone;
`--layer mutation` also runs any missing unit prerequisite. `all` is the default.
`--force` reruns current results as well as pending work.

## Selection and reuse

A fresh, complete graph without extraction errors or warnings is required. Refresh
it after source or test changes. Each declared production file is one batch. CIS
follows callers to identify test files, then follows implementation and test
dependencies to include shared code and fixtures in that batch's input identity.
New test files in the same graph components enter selection automatically.

For C# files, class names from the graph produce a VSTest filter intersected with
`fullTestFilter`. When the graph cannot narrow that selection, CIS uses the full
configured filter and hashes all graph inputs. Other languages use this full-filter
path; language-specific narrow filters are not inferred.

The graph supplies static candidates, not measured runtime coverage or proof of
assertion quality. Review reflection, dynamic dispatch and process boundaries when
choosing scope and tests. Stryker's native coverage analysis remains responsible
for test-to-mutant coverage during execution.

Reuse checks relevant source and test bytes, dependency relationships, graph
schema/extractors, the whole profile, declared configuration and tool files, the
installed testing/helper assemblies, platform and inherited environment identity.
Inherited environment values are hashed and are not saved. A graph build ID change
alone does not invalidate otherwise unchanged work. Native report hashes and parsed
outcomes are rechecked before reuse.

Failed or aborted unit results, missing reports, source mismatches, pending/runtime
error outcomes and timeouts cannot become reusable completions. Completed native
survivor and no-coverage findings remain saved until relevant inputs change or a
forced rerun is requested. They retain their original status and score contribution.
An exact native file with an empty mutant array adds no kills or denominator;
filter placeholders and files whose mutants are all ignored or compile errors
cannot use that empty-file rule.

## Profile

Supply repository-relative source, test, configuration and report paths. The input
inventory must cover the required scope. `toolFiles` must pin the absolute native
executables and other tool code or manifests relevant to the run. Each command's
executable must be absolute and included in `toolFiles`. Project files, dependency
locks, runtime settings, shared test setup and relevant fixtures belong in the
configuration inventory or graph dependencies.

This illustrative .NET profile requires the specified installed tools and projects:

```json
{
  "schemaVersion": 1,
  "id": "service-unit",
  "workingDirectory": "src/Service",
  "sourceFiles": ["src/Service/Policy.cs", "src/Service/Counter.cs"],
  "testFiles": ["tests/Service.Tests/PolicyTests.cs", "tests/Service.Tests/CounterTests.cs"],
  "configFiles": ["global.json", "src/Service/Service.csproj", "tests/Service.Tests/Service.Tests.csproj", "src/Service/stryker.json"],
  "toolFiles": ["/opt/dotnet/dotnet", "/opt/stryker/Stryker.CLI.dll", "/opt/stryker/Stryker.Core.dll"],
  "fullTestFilter": "FullyQualifiedName~Service.Tests.",
  "unitCommand": ["/opt/dotnet/dotnet", "test", "../../tests/Service.Tests/Service.Tests.csproj", "-c", "Release", "--no-restore", "--filter", "{testFilter}", "--logger", "trx;LogFileName=unit.trx", "--results-directory", "{output}"],
  "unitReportPath": "unit.trx",
  "unitResultFormat": "trx",
  "mutationCommand": ["/opt/dotnet/dotnet", "/opt/stryker/Stryker.CLI.dll", "--config-file", "{mutationConfig}", "--output", "{output}"],
  "strykerConfigFile": "src/Service/stryker.json",
  "canarySourceFile": "src/Service/Policy.cs",
  "minimumCanaryKills": 1,
  "mutationReportPath": "reports/mutation-report.json",
  "recoveryCommand": ["/opt/dotnet/dotnet", "build", "../../tests/Service.Tests/Service.Tests.csproj", "-c", "Release", "--no-restore", "-t:Rebuild", "-m:1", "-nr:false"],
  "environment": {"MSBUILDDISABLENODEREUSE": "1", "DOTNET_CLI_USE_MSBUILD_SERVER": "0", "UseSharedCompilation": "false"},
  "breakThreshold": 90,
  "timeoutSeconds": 300,
  "recoveryTimeoutSeconds": 120,
  "maxRunBytes": 134217728,
  "maxRetainedBytes": 268435456,
  "minFreeBytes": 1073741824
}
```

`mutationCommand` is optional for unit-only profiles. Mutation requires a
`recoveryCommand` that restores original build outputs. Commands are executable and
argument arrays. `{sourceFile}` is relative to `workingDirectory`; `{testFilter}` is
the selected filter; `{output}` is a fresh owned directory. Unit reports support
TRX and JUnit. Mutation reports use the existing Stryker JSON adapter.

With `strykerConfigFile`, CIS copies the pinned native template into the owned work
directory and sets `mutate` and `test-case-filter` for that batch. Use
`{mutationConfig}` for its generated path and include the original template in
`configFiles`. Other native settings stay intact. The template itself is unchanged.
This uses Stryker.NET's configuration field for the test filter; no unsupported
test-filter command-line option is required.

Review the native mutation configuration for the required level, ranges, exclusions,
coverage and timeout policy. File/text checks do not establish that all possible
mutants were generated. Targets supply profiles; CIS does not load target assemblies
as executable plugins.

## Bounds and recovery

Defaults are four file batches and 1,200 seconds per invocation. The maximums are
512 batches and 3,600 seconds. Scheduling reserves time for baseline recovery;
native guard startup, cleanup and final publication can add overhead. Each native
phase has its own profile timeout. A large interrupted file repeats at most that
file's bounded attempt; there is no per-mutant checkpoint inside a file.

Unit and mutation commands use the shared native process/storage guard and its
short owned temporary directories. `maxRunBytes` bounds a native phase and its
owned scratch. `maxRetainedBytes` measures `.cis/local/testing`, including saved
results. Existing unrelated reports count toward the cap and are not deleted to
make room. The command shares the existing mutation execution lease.

Before instrumentation, CIS durably marks recovery as required. After every mutation
attempt, including a missing report or hard timeout, it runs `recoveryCommand` and
the unit command with `fullTestFilter`. A failed recovery holds cached progress.
An interrupted invocation records the previous native guard's maximum activity
window; another invocation refuses to overlap it, then performs any required
recovery before reuse. Native infrastructure or cleanup failures remain failures.

## Saved results and scores

State lives under `.cis/local/testing/incremental/<id>/`. It contains one current
unit report and one current mutation report per source batch, compact state and
the latest invocation summary. Replaced reports and reports for retired batches
are removed. Native work trees are deleted; failures retain at most the last
128 KiB of each standard-output/error log. Successful postflight captures are
transient. Saved current reports are a reusable cache, not an attempt archive.

`knownScore` covers current readable native findings only. `completeScopeScore`
remains absent while any required source batch is unfinished or recovery is on
hold. Native timeout findings stay visible and receive no conservative kill credit.
`thresholdMet` also requires no uncovered mutants. `qualified` is always false:
the declared development profile does not certify complete release scope.

Exit codes are `0` for a completed requested layer or a readable plan, `2` for an
invalid plan, `3` for remaining work after a scheduling limit, and `4` for execution,
recovery, state or evidence failure. A completed mutation scope can return `0` with
survivors; inspect the score and threshold separately.

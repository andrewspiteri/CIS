---
title: "cis test incremental"
type: command-reference
status: Draft
owner: "Andrew Spiteri"
last_reviewed: "2026-10-28"
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
With `--layer unit`, a healthy refresh preserves compatible completed mutation
reports, including those for batches outside the current execution budget.
Changed inputs or a failed unit refresh invalidate the affected mutation result.
A damaged saved unit report also requires a new mutation check. A unit-only run
reports `reusedMutationBatches: 0` because that counter describes the requested
layer; preserved mutation reports remain available to a later mutation run.

## Selection and reuse

SSH connection identifiers and shell session bookkeeping do not invalidate saved
results. CIS omits `SSH_CLIENT`, `SSH_CONNECTION`, `SSH_TTY`, `XDG_SESSION_ID`,
`TERM_SESSION_ID`, `WT_SESSION`, `SHLVL`, `_`, `PWD` and `OLDPWD` from ambient
identity. Native commands receive empty values for those variables, except `PWD`
is their working directory and `SHLVL` is `0`. Explicit profile environment values
override these defaults and remain pinned inputs. Other inherited variables,
including `PATH` and `SSH_AUTH_SOCK`, remain identity inputs unless the profile
overrides them. Inherited values are hashed, never saved in reports.

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

Native build commands must compile the declared inputs. After restoring files
with timestamps older than existing build outputs, run a clean or forced baseline
build before execution. An incremental compiler may otherwise reuse stale
binaries. CIS checks source and report identity; those checks do not establish
that an arbitrary native build command rebuilt its binaries.

Failed or aborted unit results, missing reports, source mismatches, pending/runtime
error outcomes and timed-out invocations cannot become reusable completions. Completed native
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

### Revalidate a compatible runner update

After reviewing a testing-module update for compatibility, use `revalidate` to
preserve completed results whose other inputs still match. Supply the previous
installed `Cis.Modules.Testing.dll` outside the target repository, with its original
`Cis.Abstractions.dll` beside it, and record the review reason. Review compatibility
of both assemblies. For example, correcting a false storage-scan failure leaves earlier
completed native verdicts unchanged.

```text
cis test incremental revalidate --repo <repository> --profile <relative.json> --previous-testing-assembly <absolute-previous-Cis.Modules.Testing.dll> --reason "Reviewed guard correction; native assessment rules unchanged." --format agent
```

CIS reconstructs the previous producer's input identity, rechecks native reports
and source, then updates eligible cache fingerprints. Reports and original native
execution records remain unchanged. Changed inputs, failed stages, unfinished
execution and recovery holds cannot acquire completion through this command.
The reason records the compatibility review; CIS does not infer compatibility
from that text. A changed result interpretation needs its own policy review.

Revalidation also saves a structured producer history on each changed stage. Each
link binds the previous and current testing/helper hashes, input fingerprints,
unchanged report hash, environment digests and compatibility review. Histories are
limited to eight links. Keep the reviewed previous binary pairs at their recorded
locations while refinement still relies on their native receipts. Refinement
rechecks those files and the complete chain; a text diagnostic alone cannot
authorize an older helper. Refresh native results when the history limit is reached.

For the one-time transition from the original full-environment cache identity,
add `--legacy-session-environment` while still in the original launch environment.
Review that the earlier native work did not rely on the now-canonical session
values, or declare required values in the profile and rerun affected work. The
flag reconstructs the old identity before checking reports; it cannot recover a
lost original environment or authorize reuse when other inputs changed. Later
compatible updates use ordinary `revalidate` without this flag.

Defaults are four file batches and 1,200 seconds per invocation. The maximums are
512 batches and 3,600 seconds. Scheduling reserves time for baseline recovery;
native guard startup, cleanup and final publication can add overhead. Each native
phase has its own profile timeout. A large interrupted file repeats at most that
file's bounded attempt; there is no per-mutant checkpoint inside a file.

For a large file, `run --native-timeout-seconds <1-3600>` overrides the invocation
guard within the command's remaining wall budget. Use one file batch when it needs
that full budget. The override is recorded in diagnostics and does not alter the
native per-mutant timeout or invalidate completed input-matching results. Native
timeouts inside Stryker remain configured in its pinned template.

Unit and mutation commands use the shared native process/storage guard and its
owned temporary directories. On Linux, scratch uses a short directory under the
host temporary root so repository paths cannot exhaust Unix-domain socket limits.
Set the invoking process's `TMPDIR` to a short owned location when needed.
`maxRunBytes` bounds a native phase and its scratch. `maxRetainedBytes` measures
`.cis/local/testing`, including saved results, plus active scratch outside that
directory. Free-space checks cover both filesystems. Existing unrelated reports
count toward the cap and are not deleted to make room. The command shares the
existing mutation execution lease.

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
are removed. Native work trees are deleted; failed invocations and completed
mutation reports containing timeouts retain at most the last 128 KiB of each
standard-output/error log. Successful recovery and postflight captures are
transient and do not overwrite those retained tails. Saved current reports are
a reusable cache, not an attempt archive.

`knownScore` covers current readable native findings only. `completeScopeScore`
remains absent while any required source batch is unfinished or recovery is on
hold. An explicit mutant timeout in a normally completed native report remains a
cached miss after healthy recovery. It receives no conservative kill credit and
keeps `thresholdMet` false. An invocation killed by its guard is unfinished work
and cannot use this rule. `thresholdMet` also requires no uncovered mutants. `qualified` is always false:
the declared development profile does not certify complete release scope.

### Verify a completed timeout rerun

`cis test incremental refine` verifies saved results without executing tests. It
can give separate credit when an original native `Timeout` has a completed native
`Killed` result for the exact same source, mutant definition and current inputs.
It does not change the raw report, remove unresolved mutants, or qualify a release.

```text
cis test incremental refine --repo . --profile original.json --rerun-profile rerun.json --reason "Reviewed narrower test filter and exact source ranges." --format json
```

Both profiles must belong to the same repository and have current, completed unit
and mutation results with healthy recovery and postflight. Tests, dependencies,
configuration, tools and environment must agree. Supported profile differences
are the profile ID, a source subset and its canary, the reviewed test filter, and
native `--mutate` or `-m` arguments selecting that source and bounded character
ranges, and a shorter outer invocation guard (`timeoutSeconds`). Other command or
setting differences fail verification. A shorter outer guard still requires a
completed native execution with healthy recovery and postflight; a guard timeout
or abort cannot gain credit. Native Stryker timeouts and test timeout settings
must remain unchanged. A longer outer guard is rejected.

Killing test IDs must have matching native definitions in both reports, including
current test source text and valid locations. Only original `Timeout` to rerun
`Killed` outcomes can gain credit. Runtime errors, pending work, changed definitions
and missing or damaged evidence remain failures.

When the rerun changes the test filter, verification returns `needs-baseline`
with exit code `3` until every original unit batch has been refreshed after the
rerun. Start the original profile's unit layer with `--force`; resume without
`--force` if a scheduling limit leaves batches pending. This preserves healthy
mutation caches. Then invoke `refine` again. No refinement credit is published
while that refresh is pending.

A successful verification saves one current sidecar per rerun profile under
`refinements/` in the original profile's state directory. It records raw and
effective counts separately, original and rerun report hashes, input and producer
identities, baseline bindings, and the verified native killing tests. The sidecar
is limited to 1 MiB and replaces the previous attestation for that profile pair.
Invoke `refine` again to recheck current evidence before relying on saved credit;
a saved sidecar alone does not establish that its inputs remain current.

Exit codes are `0` for a completed requested layer or a readable plan, `2` for an
invalid plan, `3` for remaining work after a scheduling limit, and `4` for execution,
recovery, state or evidence failure. A completed mutation scope can return `0` with
survivors; inspect the score and threshold separately.

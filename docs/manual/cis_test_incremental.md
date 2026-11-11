---
title: "cis test incremental"
type: command-reference
status: Draft
owner: "Andrew Spiteri"
last_reviewed: "2026-11-07"
review_cadence: on incremental execution contract change
cis:
  stable_id: change-impact-studio:manual:cis-test-incremental
---

# `cis test incremental`

Run pending unit tests and mutation work in bounded file batches. CIS saves each
completed result immediately and reuses it while its relevant inputs still match.
An interrupted file batch repeats that file; earlier completed files remain saved.
This command provides development feedback. It does not replace release qualification.

## Required settlement

The command retains valid native reports before final accounting. An actual
child/root `Finish` failure or unresolved storage settlement returns
`settlement-required` with exit code `4`; the retained latest outcome and downstream
verification also stay held. Counts and threshold observations remain available.
Read-only planning/verification check the original work reference rather than
treating saved native success as proof that accounting finished. Reconcile that
exact execution through its original authority; a later run reuses still-current
reports instead of executing them again solely for bookkeeping.

`run` and `revalidate` keep the native output lease through final publication,
including failure handling and supported cancellation. If another invocation owns
that lease, the new invocation returns failure and settles its admitted work
without replacing the current state or latest result.

The same settlement rule applies to governed `revalidate` and `refine` metadata
receipts. Their successful native/derived observations remain distinct from the
receipt's overall status. A failed metadata `Finish` does not publish a completed
receipt or clear an unsettled source/rerun. Source-variant verification also checks
the retained phase settlements before reporting its partial verified evidence.

A successfully settled child remains valid while its ancestor is running. A
next-operation reflection or capacity hold after successful settlement does not
invalidate completed evidence. Process uncertainty, required restoration, ownership
and capacity checks remain independent.

## Native work authority and representative readiness

`run` admits native work through the existing approved change/task and finite work
and storage allocation before costly preparation. It validates inherited
`CIS_WORK_AUTHORITY`, `CIS_WORK_CHANGE`, `CIS_WORK_TASK`, `CIS_WORK_ACTOR` and
`CIS_WORK_PARENT_EXECUTION` against durable authority. Preserve an inherited parent.
For a standalone invocation, use an unambiguous approved binding or explicit
`--change <id> --task <id> --actor <controller>`; add `--authority <repository>` for
a linked owned target and `--parent-execution <id>` when needed. Profile ceilings
and `--max-seconds` tighten existing capacity; they never grant a fresh allowance.
Missing or ambiguous ownership holds before native preparation. Read-only `plan`,
`status` and `verify` do not launch native work.

`refine` and `revalidate` publish derived metadata. They use the same inherited or
explicit work association flags, a short existing allowance and exact metadata
growth reservations, without launching native tests. Completed-state association
does not allocate capacity or waive a current hold. `--recovery-of` is rejected
for these metadata operations; restore through the original `run` route.

Before adopted pending mutation work expands, set
`correctnessPilotProfile` to a maintained small native representative mutation
profile. Run its existing canary route and inspect the native controls, costs and
actual child coverage as described in
[`cis test mutation run`](cis_test_mutation_run.md#representative-native-profile).
The actual target commands, native configuration, source/test project XML, tools
and environment must match its exercised path; unknown or changed paths hold.
The controller records the first outcome and approach decision through existing
[`agent work progress`](cis_agent_work.md#record-progress), with the returned
`native-pilot:<basis>` evidence reference and current revision, before expansion.
Routine controller assessment needs no new human acknowledgment. A per-file
positive-kill canary remains separate and cannot supply this broader proof.

Genuinely non-adopted diagnostics retain explicit unaccounted coverage. They cannot
supply governed accounting or complete qualification. Current cached evidence can
still be inspected and reused without another native launch; a missing or changed
pilot blocks actual expansion, rather than deleting compatible reports.

Mutation profiles designate a `canarySourceFile` with tests known to detect at
least `minimumCanaryKills` native mutants. That file runs first and its successful
result can be reused. A canary with no required kills stops further mutation work;
check the native adapter, mutant activation and assertions before continuing.
Unit-only profiles and `--layer unit` do not require a mutation pilot or per-file
mutation canary. Their existing native unit health and currentness checks still apply.

```text
cis graph build --repo <repository> --format agent
cis test incremental plan --profile <relative.json> --repo <repository> --format agent
cis test incremental run --profile <relative.json> --repo <repository> --layer all --max-batches 4 --max-seconds 1200 --format agent
cis test incremental status --profile <relative.json> --repo <repository> --format agent
cis test incremental run --profile <relative.json> --repo <target> --change CIS-0001 --task WORK-090 --authority <authority> --actor "Existing controller" --layer all --max-batches 1 --max-seconds 300 --format json
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

Work owner, attempt, parent and run IDs are invocation coordinates, not material
test inputs. The five `CIS_WORK_*` authority variables above do not invalidate
compatible evidence and cannot be supplied through profile `environment`.
Validated invocation context still reaches actual native launches. Unchanged graph
rebuilds and unrelated documentation do not rerun tests. Relevant code, tests,
dependencies, native configuration, tools and producer interpretation still do.

SSH connection identifiers and shell session bookkeeping do not invalidate saved
results. CIS omits `SSH_CLIENT`, `SSH_CONNECTION`, `SSH_TTY`, `XDG_SESSION_ID`,
`TERM_SESSION_ID`, `WT_SESSION`, `SHLVL`, `_`, `PWD` and `OLDPWD` from ambient
identity. Native commands receive empty values for those variables, except `PWD`
is their working directory and `SHLVL` is `0`. Explicit profile environment values
override these defaults and remain pinned inputs. Other inherited variables,
including `PATH` and `SSH_AUTH_SOCK`, remain identity inputs unless the profile
overrides them. Inherited values are hashed, never saved in reports.
Ambient `TEMP`, `TMP` and `TMPDIR` are also excluded because the native executor
replaces them with its owned temporary directory. Explicit profile values remain
material and are honored by execution.
`CODEX_TOOL_CALL_ID` and `CODEX_APP_TOOLS_PIPE_PATH` are also canonicalized to empty
as invocation bookkeeping; explicitly declared profile overrides remain material.

A fresh, complete graph without extraction errors or warnings is required. Refresh
it after source or test changes. Each declared production file is one batch. CIS
follows callers to identify test files, then follows implementation and test
dependencies to include shared code and fixtures in that batch's input identity.
New test files in the same graph components enter selection automatically.
External metadata symbols may record repository call sites. Those observations
do not make one caller depend on another caller's file. Compiler-bound repository
declarations, including partial declarations and interface relationships, remain
dependency inputs. Pin external dependency and runtime identity through the
declared configuration and tool inventory.

For C# files, class names from the graph produce a VSTest filter intersected with
`fullTestFilter`. An outer dot-qualified class predicate replaces nested predicates
that it already covers. Nested-only fixtures retain their own predicates. When
the graph cannot narrow that selection, or the filter exceeds 32,768 characters,
CIS uses the full
configured filter and hashes all graph inputs. Other languages use this full-filter
path; language-specific narrow filters are not inferred.

The graph supplies static candidates, not measured runtime coverage or proof of
assertion quality. Review reflection, dynamic dispatch and process boundaries when
choosing scope and tests. Stryker's native coverage analysis remains responsible
for test-to-mutant coverage during execution.

Reuse checks the parsed inputs that affect the selected stage: relevant source,
tests and dependencies, the effective command/filter, configuration, declared
toolchain, platform and effective environment. Unit identity excludes profile JSON
formatting, retention/storage settings and mutation-only settings. Profile and
mutation-only template files cannot re-enter unit identity as raw graph fallback
dependencies. A graph build ID change alone does not invalidate unchanged work.
Inherited environment values are hashed, never saved.

When checking a saved command, CIS compares absolute executable paths in their
canonical form and keeps every other argument exact. This accepts the runner's
normalized spelling of the same executable for an unchanged profile. The declared
tool, filter and bounded output requirements still apply.

Accepted stages record assessment contract version **2**. CIS producer hashes are
retained separately as provenance; unrelated CIS assembly changes do not change a
consumer result's identity. Status, reuse and verification apply the same current
result checks, including native execution facts and the retained report's hash and
parsed counts. Keep the current native report and compact input/result metadata.
Deleting that report makes its result noncurrent. `verify --layer unit` verifies
accepted unit scope even when the profile also describes mutation work.

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

The incremental route still prepares one mutation source per invocation. Measure
that setup cost before expansion. When compatible sources share expensive project
preparation, the existing multi-source mutation profile can group them in one
native invocation; retain each source's native coverage and complete-scope facts.
Zero-definition files need exact empty native scope, not a separate compilation
solely to increase batch count. This command introduces no additional scheduler.

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
  "correctnessPilotProfile": "native-pilot.json",
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

Ordinary unit-only interruption does not require mutation restoration. Under the
existing native invocation lock, CIS verifies the exact saved work references.
After their accounting is settled, it clears stale activity markers and retries
only unfinished or invalidated unit batches, preserving current completed results.
Live conflicting work still blocks launch, even after a displayed deadline.

On Linux, supported native cancellation stops the worker's recorded session as
well as verified driver/controller processes. After cancellation, reconcile the
stopped tree before retrying; the driver's absence alone does not establish that
its children stopped. Controlled retirement below is an alternative when its
conditions apply.
The retained session covers the existing trusted runner, including process groups
within it, rather than code that deliberately creates another session. Missing
historical session bindings remain unknown and can use controlled retirement when
its conditions apply. The same saved unit results and finite accounting survive.
After exact settlement, retry can retire that invocation's recorded short temporary
directory through the existing storage authority without waiting for its old
scratch timeout. Missing or ambiguous scratch ownership remains held.

For a disposable legacy unit invocation with incomplete cleanup observations,
first clean up its processes or retire its execution environment and shared
resources, then use the controlled `abandon-native-unit` disposition described in
[`cis agent work`](cis_agent_work.md#retry-interrupted-disposable-unit-work).
It preserves unknown facts and charges legitimate cost; it does not manufacture a
report or refund an attempt. The normal unit retry needs no `recoveryCommand`.

### Revalidate a compatible runner update

Versioned accepted results remain reusable across compatible CIS installations
without a manual producer transition. The assessment contract and material inputs
must still match. Old CIS installations and chains of DLL paths are not required
for later status, reuse or verification.

`revalidate` can inspect compatible results or migrate a provable unversioned
legacy result without running tests. Record the actual compatibility reason. The
optional `--previous-testing-assembly` supplies original producer digests when
legacy evidence lacks them; it is a one-time discovery input, not an installation
retention requirement. Existing compact legacy provenance can also supply its
digests. Missing legacy identity proof remains held rather than receiving a pass.

```text
cis test incremental revalidate --repo <repository> --profile <relative.json> --reason "Reviewed compatible result assessment contract and unchanged material inputs." --format agent
```

CIS rechecks the native report, its original execution facts and the relevant
inputs. Eligible legacy results acquire the current contract and compact producer
metadata while keeping their report and native execution records unchanged.
Versioned results do not accumulate a producer chain or require a native refresh
after eight updates. Changed material inputs, failed/incomplete stages and unsettled
work cannot acquire completion through compatibility text. A change in result
interpretation requires an explicit contract-version change.

For the one-time transition from the original full-environment cache identity,
add `--legacy-session-environment` while still in the original launch environment.
Review that the earlier native work did not rely on the now-canonical session
values, or declare required values in the profile and rerun affected work. The
flag reconstructs the old identity before checking reports; it cannot recover a
lost original environment or authorize reuse when other inputs changed. Later
compatible CIS updates need no manual transition. If you choose to inspect
versioned results with `revalidate`, omit this flag.

Defaults are four file batches and 1,200 seconds per invocation. The maximums are
512 batches and 3,600 seconds. Scheduling reserves time for baseline recovery;
native guard startup, cleanup and final publication can add overhead. Each native
phase has its own profile timeout. A large interrupted file repeats at most that
file's bounded attempt; there is no per-mutant checkpoint inside a file.
For governed work, the owner, correction and actual parent remainder additionally
bound preparation, execution and result admission. A child uses its positive
remaining time after preparation; a new file, resume or run ID does not refill
capacity. Inspect `workControl.work` for consumed/reserved/remaining work and each
retained native stage's `workReference` for the actual launch.

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
Governed storage also counts the allocation's total declared roots and exact
scratch, including declared sibling build outputs. Local caps and free-space
requirements remain additional constraints. Read owned/protected/reserved capacity
through the existing artifacts inventory and plan, then use delegated cleanup
authority with the existing confirmation mechanism. Protected current evidence and
accounting/recovery state cannot be removed to create capacity. Sampling can
overshoot between checks and does not provide machine-wide enforcement.

Before instrumentation, CIS durably marks recovery as required. After every mutation
attempt, including a missing report or hard timeout, it runs `recoveryCommand` and
the unit command with `fullTestFilter`. A failed recovery holds cached progress.

When only recovery failed after an admissible completed native invocation, retry
the same profile without `--force`. CIS rechecks the exact input fingerprint,
report hash, parsed outcomes, native command and producer identity, then retries
the pinned recovery and full-filter postflight budgets. It can reuse that native
report only after both phases pass and inputs remain unchanged. The earlier
failed receipts are retained in a per-batch recovery history, limited to eight
attempts and 256 KiB; reaching either bound holds the retry. A native invocation
timeout, abort, damaged report or known input-change hold cannot gain completion
through recovery. An explicit mutant Timeout in an otherwise completed report
remains a conservative miss.

A reviewed compatible producer update may revalidate such a report's identity
while recovery is pending. This does not complete the stage or clear its recovery
hold. Run the same profile afterward to obtain actual healthy recovery receipts.
`--native-timeout-seconds` overrides only the mutation invocation guard; it does
not extend `recoveryTimeoutSeconds`.
An interrupted invocation records the previous native guard's maximum activity
window; another invocation refuses to overlap it, then performs any required
recovery before reuse. Native infrastructure or cleanup failures remain failures.
Governed pending recovery enters restoration-only execution under its retained
owner/reference and unchanged recipe before any new batch. If local recovery
context is lost, `run --recovery-of <retained-execution-id>` requests only that
pinned restoration and full-filter healthy postflight; it does not rerun mutation
or grant broader work. A restored baseline does not manufacture missing results.
The versioned recovery recipe pins declared environment values, commands, native
configuration, tools and platform separately from full material evidence identity.
Unrelated invocation environment changes can permit bounded restoration while old
reports remain noncurrent. Changed declared recovery inputs hold before launch;
legacy opaque recipes do not gain an automatic compatibility migration.

## Saved results and scores

Keep native `validEvidence`, `thresholdMet` and readiness separate. A native report
can remain valid at the unchanged threshold while required-child, infrastructure,
restoration or cleanup obligations hold readiness. Incremental summaries and
verification receipts always retain `qualified: false`; scope completeness or
cache reuse cannot relabel them as complete production qualification.

State lives under `.cis/local/testing/incremental/<id>/`. It contains one current
unit report and one current mutation report per source batch, compact state and
the latest invocation summary. Eligible replaced reports and reports for retired
batches are removed only after the existing owned-storage authority permits the
destructive change. Required evidence from another owner, active output and
unresolved recovery evidence retain their original bytes when that authority is
missing; a forced run or narrower profile cannot override that protection. Use
the existing authorized owner or delegated supersession controller to replace
protected evidence. Compatible reuse and ordinary unprotected cache updates remain
available. Native work trees are deleted; failed invocations and completed
mutation reports containing timeouts retain at most the last 128 KiB of each
standard-output/error log. Successful recovery and postflight captures are
transient and do not overwrite those retained tails. Saved current reports are
a reusable cache, not an attempt archive.
Governed storage protection follows these current state/report references.
Superseded report claims are released only after the replacement state is published
and the original native ownership and restoration obligations are safely resolved.

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
or abort cannot gain credit. A longer outer guard is rejected.
`correctnessPilotProfile` references may differ only when each original/rerun
native path independently has current representative proof and the required
controller assessment. This does not relax the exact source, configuration,
native report or producer checks for refinement.

An explicit rerun profile may set `rerunAdditionalTimeoutMilliseconds` to give a
bounded diagnostic more time inside Stryker. The value must be 1–60,000 ms, at
least the template's explicit positive integer `additional-timeout`, and below
the rerun's outer invocation guard. For example, a rerun may use `30000` with an
unchanged template containing `15000`. CIS changes only that key in the generated
mutation configuration; the template and default budget remain unchanged. The
original profile must omit this field or set it to null. All other native and
test timeout settings must agree. The override is part of the rerun's input
identity, and the saved native command must still match that profile. It cannot
turn a native Timeout, guard timeout or incomplete execution into kill credit;
only a completed native Killed result can supply the separate credit.
For governed execution, the referenced representative pilot must have exercised
the effective larger `additional-timeout` configuration. CIS compares that one
recognized rendered override with the pilot's native configuration; an unchanged
smaller-timeout pilot does not qualify the rerun. Keep the same work owner and
finite allowance when obtaining that bounded representative evidence.

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

### Verify an exact source variant

`cis test incremental verify-source-variant` checks saved diagnostic evidence for
one original native `Survived` definition. It compares an original/pass attempt,
the exact source replacement/fail attempt, and a restored/pass attempt. It does
not run tests, change raw reports or caches, or apply mutation score credit.

```text
cis test incremental verify-source-variant --repo <repository> --manifest .cis/source-variant.json --format json
```

Prepare three separate, non-nested snapshots with identical unit-only profiles,
current tests, dependencies, build settings, tools and environment. Declare one
production source file. Only that file's exact native span/replacement may differ
in the variant snapshot; the restored source must match the original bytes. Run
each profile through `cis test incremental run --layer unit`. The expected failed
variant remains a failed ordinary unit attempt; this verifier checks that saved
attempt separately. Each attempt must execute exactly one native test case with
the same GUID and method recorded in the original Stryker report.

Inside that test, use `SourceVariantBodyCapture.Encode(subjectAssembly,
sourceDocument, testAssembly, testSourceDocument)` from `Cis.Abstractions` and write
its returned line through the native test's output channel. Retain the actual
subject/test assemblies and their external portable PDB files at the observed
paths. CIS checks their hashes, module identities, PE-bound PDB checksums and
SHA256 source-document checksums. A console probe, supplied completion flags,
stale binaries or a different test body cannot replace this evidence. Native
timeouts, incomplete hosts and retained input-change or execution holds fail
verification; no assertion-message whitelist is used.

Illustrative manifest; replace the paths, report hash and GUID with actual evidence:

```json
{
  "schemaVersion": 1,
  "evidence": {
    "repositoryPath": ".",
    "reportPath": ".cis/original-mutation-report.json",
    "reportSha256": "<original-report-sha256>",
    "sourceFile": "src/Subject.cs",
    "subjectAssemblyPath": "bin/Release/net10.0/Example.Tests.dll",
    "mutantId": "17",
    "nativeTestId": "<native-test-guid>",
    "original": { "repositoryPath": ".", "profilePath": "unit-proof.json" },
    "variant": { "repositoryPath": "/work/variant", "profilePath": "unit-proof.json" },
    "restored": { "repositoryPath": "/work/restored", "profilePath": "unit-proof.json" }
  }
}
```

The manifest, report and current source paths belong under `--repo`;
`evidence.repositoryPath` must identify that same repository. Each phase's profile
path belongs under its named snapshot. A snapshot outside `--repo` needs an
explicit absolute path. Manifests are limited to 256 KiB, reject duplicate or
unknown fields, and cannot supply approval flags.

Output supports `json`, `human` and `agent`. Exit `0` means this diagnostic check
passed; `qualified` stays false and `partial` stays true. Invalid manifests return
`2`; missing, stale or unverifiable evidence returns `4`. The JSON receipt is
limited to 1 MiB and includes pinned inputs and phase/body identities. It supplies
no automatic mutation credit or complete-scope qualification.

### Verify saved evidence for another tool

`cis test incremental verify` returns a current evidence receipt without running
tests or changing saved reports, state or refinement sidecars. Another tool can
use the JSON receipt and its referenced raw reports as inputs to its own scope
and acceptance checks.

```text
cis test incremental verify --repo . --profile original.json --refinement-profile rerun.json --format json
```

Omit `--refinement-profile` to verify raw outcomes alone. Repeat the option for
additional completed rerun profiles. CIS recomputes native timeout credits from
those profiles and their reports; it does not use saved sidecar counts or
completion claims. A mutant receives at most one added kill across reruns.

Verification requires a complete current declared scope, healthy native unit
execution, and healthy mutation recovery and full-filter postflight receipts.
Original mutation profiles must select full source files with an explicit
`Complete` mutation level. Native ranges and native incremental filtering cannot
establish full-file original evidence. If a native configuration is used, it must
be the declared template rendered by CIS, selected once with `--config-file` or
`-f`; undeclared implicit or alternate configurations fail verification.
Every counted native kill must identify
current killing-test source and a valid test definition. Changed inputs, damaged
reports, incomplete executions and unverified producer identities fail the check.

CIS retains the current unit XML for each source batch. Historical full-filter
postflight XML is deleted after successful validation; the receipt explicitly
reports that only its native execution receipt is available. A narrowed rerun
still requires the original profile's fresh unit baseline described above.

The receipt is limited to 1 MiB. It deduplicates source, test, dependency,
configuration and tool hashes, binds raw reports and native stages, and reports
raw and effective counts separately. Commands and environment values appear only
as digests. No repository archive or raw report copies are created. The receiving
tool must obtain the referenced reports and independently match the receipt's
source and input closure to its required scope. A successful check returns `0`
even when the score is below the threshold; `qualified` remains false.

Exit codes are `0` for a completed requested layer or a readable plan, `2` for an
invalid plan, `3` for remaining work after a scheduling limit, and `4` for execution,
recovery, state or evidence failure. A completed mutation scope can return `0` with
survivors; inspect the score and threshold separately.
Human and agent output include representative readiness, controller assessment,
restoration status, evidence reference and next action when a pilot record applies.
These observations do not change the incremental result's unqualified scope.

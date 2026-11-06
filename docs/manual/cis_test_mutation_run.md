---
title: "cis test mutation run"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-11-06"
review_cadence: "on mutation runner contract change"
cis:
  stable_id: change-impact-studio:manual:cis-test-mutation-run
---

# `cis test mutation run`

Execute a native mutation command through CIS's shared process and storage guard.
Each target supplies a JSON profile; CIS does not contain target-specific assertion
rules, source offsets or test controllers.

When compilation dominates, use this generic command or the documented direct
native runner to group sources sharing a compilation and compatible test context.
The profile can declare multiple `sourceFiles`; the current
[`cis test incremental`](cis_test_incremental.md) mutation path selects one source
file per invocation. Choose it when that granularity is useful. This generic
command executes and assesses one complete native invocation.

```text
cis test mutation run --profile <repository-relative.json> [--kind diagnostic|canary|qualification] [--repo <path>] [--max-seconds 3600] [--change <id> --task <id> --actor <controller>] [--authority <path>] [--parent-execution <id>] [--recovery-of <execution-id>] [--format human|json|agent]
```

The default kind is `diagnostic`. Its native score is visible, but it cannot qualify
the complete scope. `canary` runs the profile's small prerequisite command.
`qualification` requires declared production files and a separate canary command;
it stops if that canary fails before executing the complete profile command.
Qualification applies to the declared profile, not to whole-release readiness.

## Work authority and expansion

An initialized or adopted repository needs its installed work and storage authority
before native preparation. CIS validates the inherited `CIS_WORK_AUTHORITY`,
`CIS_WORK_CHANGE`, `CIS_WORK_TASK`, `CIS_WORK_ACTOR` and, when present,
`CIS_WORK_PARENT_EXECUTION` against durable state. Do not clear an inherited parent
to obtain a standalone allowance. Otherwise use an unambiguous approved binding
or the explicit options above. A missing or ambiguous owner holds before native
preparation and reports the next action. These options and profile limits spend
existing capacity; they do not allocate time, attempts or storage.

```text
cis agent work status CIS-0001 --task WORK-090 --repo <authority> --format json
cis test mutation run --repo <target> --profile native-pilot.json --kind canary --change CIS-0001 --task WORK-090 --authority <authority> --actor "Existing controller" --max-seconds 300 --format json
```

The work result identifies the owner and attempt; `nativeExecution.workReference`
identifies the actual native launch separately from its orchestration parent. Read
consumed, reserved and remaining capacity in `workControl`; inspect owned usage
and protected evidence with [`cis artifacts inventory`](cis_artifacts_inventory.md).
Preparation, execution, result admission and required recovery share the owner;
nested active intervals count once. Failed work still costs time. Cancellation or
unknown children retain independent holds until owned cleanup is proved.

Before governed diagnostic expansion, and before any full qualification, configure
`correctnessPilotProfile` with a small representative native profile. Its retained
baseline, detected mutation, survivor/control, rejected infrastructure failure,
restoration and healthy postflight must match the actual runner path. A positive
kill canary is a separate prerequisite and cannot replace these observations.
Governed `--kind canary` requires an explicit separate `canaryCommand`; it cannot
fall back to the broad mutation command.
Genuinely non-adopted diagnostics/canaries remain available with explicit
unaccounted coverage; they do not supply governed coverage or full qualification.

Inspect the first representative result, then record the existing controller's
substantive outcome, approach and next decision using
[`cis agent work progress`](cis_agent_work.md#record-progress). Include the returned
`pilot.evidenceReference` (`native-pilot:<basis>`) in checkpoint evidence, use the
current work revision and keep the same owner. An automatic execution summary
does not award this assessment. Resume the existing profile after the assessment;
no additional human acknowledgment is required.

## Profile

This example illustrates the contract; adapt its commands and input list to the
repository's adopted native runner and policy. PowerShell 7 (`pwsh`) and the native
tool must be installed. CIS runs only its installed guard and helper assembly; the
target repository does not supply executable CIS plugins.

Required fields are `schemaVersion` (`1`), `id` (a lowercase slug), `command`,
`reportPath` and `breakThreshold`. Qualification also requires nonempty `sourceFiles`
and a separate `canaryCommand`. `workingDirectory` defaults to `.`, input lists to
empty, and `minimumCanaryKills` to `1`. Optional time/storage limits have the defaults
shown below; `healthCheckCommand` is optional. Unknown fields are rejected.
This maintained route reads Stryker-compatible JSON through the existing Stryker
adapter. Representative path compatibility requires one explicit `--config-file`
or `-f`, a pinned JSON `stryker-config` object, explicit `project` and
`test-projects`, and pinned source/test project XML. Commands using an implicit or
unrecognized configuration hold before governed expansion; configure and exercise
that supported native path first.
Native CLI `--project`, `-p` and `--test-projects` overrides cannot borrow a
representative proof: move those source/test project selections into the pinned
native configuration and retain their XML inputs. Ordinary non-adopted diagnostics
without a claimed pilot retain their existing native command support.
`environment` defaults to empty and supplies target-process variables without
changing the CIS guard's environment. Values are pinned through the profile hash.
For each native command, CIS defaults `TEMP`, `TMP` and `TMPDIR` to a fresh, short
owned directory. On Linux this uses the host temporary root, keeping Unix-domain
socket paths independent of repository depth. Set the invoking process's `TMPDIR`
to a short owned location when needed. Scratch bytes count toward both that command's
run limit and the aggregate retained limit; free-space checks cover both filesystems.
CIS removes scratch after execution. An interrupted Linux run leaves a small recovery
marker; another run waits for the previous guard's deadline before removing that
scratch under the same temporary root.
Explicit profile overrides remain honored. Files outside that command's run and
temporary directories do not count toward its per-run limit; files beneath mutation
storage still count toward the aggregate limit.

```json
{
  "schemaVersion": 1,
  "id": "service-unit",
  "workingDirectory": "src/Service",
  "environment": {"MSBUILDDISABLENODEREUSE": "1", "DOTNET_CLI_USE_MSBUILD_SERVER": "0"},
  "command": ["/opt/dotnet/dotnet", "/opt/stryker/Stryker.CLI.dll", "--config-file", "stryker-config.json", "--output", "{output}"],
  "canaryCommand": ["/opt/dotnet/dotnet", "/opt/stryker/Stryker.CLI.dll", "--config-file", "canary-config.json", "--output", "{output}"],
  "correctnessPilotProfile": "native-pilot.json",
  "reportPath": "reports/mutation-report.json",
  "breakThreshold": 90,
  "sourceFiles": ["src/Service/Policy.cs"],
  "configFiles": ["global.json", "src/Service/Service.csproj", "tests/Service.Tests/Service.Tests.csproj", "src/Service/stryker-config.json", "src/Service/canary-config.json", "tests/Service.Tests/PolicyTests.cs"],
  "minimumCanaryKills": 1,
  "timeoutSeconds": 1200,
  "maxRunBytes": 536870912,
  "maxRetainedBytes": 1073741824,
  "minFreeBytes": 1073741824
}
```

`command` and `canaryCommand` are executable/argument arrays; `{output}` expands to
the fresh owned run directory. `reportPath` is relative to that directory.
`workingDirectory`, `sourceFiles` and `configFiles` are repository-relative paths.
Include relevant tests, project/build configuration and tool manifests in
`configFiles`. CIS hashes these inputs and the profile, without copying their trees.
Commit dirty inputs when they must remain reproducible after local work changes.
Alternatively, retain the relevant dirty patch and untracked inputs with the
candidate. Production hashes alone do not identify the tests or build that produced
a result. The runner hashes only the inputs declared by the profile.
For isolated .NET campaigns, disable background build-worker reuse as shown above;
otherwise an SDK worker can outlive the native command and trigger the owned-child
cleanup hold. The runner still refuses genuine leftover children.

### Representative native profile

The referenced profile uses this same mutation profile contract, with two through
four declared fixture source files, a separate `canaryCommand`, and the following
`correctnessPilot` object. Its mutation command must produce real killed outcomes
and a deliberately surviving mutant in `controlSourceFile`. Pin relevant fixture
tests, native configurations, source/test project XML, SDK/build settings and tools.
The paths and commands below are illustrative and require a maintained native
fixture; adding this object alone supplies no evidence.

```json
{
  "baselineCommand": ["/opt/dotnet/dotnet", "test", "../../tests/Service.Tests/Service.Tests.csproj", "-c", "Release", "--no-restore", "--filter", "FullyQualifiedName~NativeControls.", "--logger", "trx;LogFileName=unit.trx", "--results-directory", "{output}"],
  "baselineReportPath": "unit.trx",
  "unitResultFormat": "trx",
  "controlSourceFile": "src/Service/SurvivorControl.cs",
  "infrastructureCommand": ["/opt/dotnet/dotnet", "/opt/stryker/Stryker.CLI.dll", "--config-file", "missing-pilot-native.json", "--output", "{output}"],
  "missingInfrastructurePath": "missing-pilot-native.json",
  "restorationCommand": ["/opt/dotnet/dotnet", "build", "../../tests/Service.Tests/Service.Tests.csproj", "-c", "Release", "--no-restore", "-t:Rebuild", "-m:1", "-nr:false"],
  "recoveryTimeoutSeconds": 30,
  "toolFiles": ["/opt/dotnet/dotnet", "/opt/stryker/Stryker.CLI.dll", "/opt/stryker/Stryker.Core.dll"]
}
```

`unitResultFormat` supports `trx` and `junit`. `toolFiles` contains bounded absolute,
unlinked paths covering the actual executable, runner dependencies and host
configuration; the short example list must be expanded for the installed tool.
The infrastructure path must remain absent and be passed literally to its native
configuration selector. The infrastructure command must use the same actual
mutation runner and arguments, changing only that explicit configuration value;
an unrelated failing program cannot supply this observation. Optional
`childTestName` claims a passing named ordinary native case plus
observed owned children. That round trip does not prove arbitrary cross-process
mutant activation. The maintained real fixture is
`tools/Cis.IncrementalValidation.Tool`; `Cis.Testing.NativeFixture` supplies synthetic
protocol faults only.

Pilot inspection rehashes raw reports and relevant runner, adapter/helper, fixture,
tool, environment and interpretation inputs; governed observations also require
their durable actual-launch records. Target native commands, configuration,
source/test project XML and declared environment must match the exercised path.
Output/source/test selectors may differ; unknown paths hold with a targeted action.
Project XML changes conservatively require another pilot even if only bookkeeping
changed. Unrelated production edits outside the pilot fixture do not invalidate it.
The pilot records preparation, native execution and result-admission costs separately;
missing observations remain missing even if a stored readiness Boolean is true.

If restoration remains required, rerun the original representative profile. CIS
uses its original work reference and unchanged recovery recipe for restoration
and healthy postflight only. If the local pilot record is missing, use explicit
`--recovery-of <retained-execution-id>` with that original profile. Neither route
runs the narrow canary, repeats instrumentation or broadens scope. `pilot.restored`
can be true while `pilot.ready` remains false because other controls are missing.
Recovery pins the exact recipe, declared environment, tools and platform separately
from full inherited-environment readiness. An unrelated invocation environment
change does not replace that recipe or grant readiness. A changed recipe or an old
opaque environment-bound recipe remains held explicitly; a local recovery Boolean
cannot migrate it or override the authoritative original execution.

For the exact affected MSTest dependency, the optional
[adapter compatibility tool](cis_mstest_adapter_compat.md) prepares a separately
pinned development overlay. Its own native controls must pass before a broad
campaign; the shared runner does not enable or infer compatibility automatically.

An optional `healthCheckCommand` uses the same argument-array format and runs after
a readable native report. Its zero exit is required when configured. The profile
owner must choose a hook that checks meaningful baseline, transport or cleanup
health; recording its exit does not prove that it checks every possible failure.

Full qualification requires every declared production file in the report, with
matching source text. Additional files may contain only ignored or compile-error
definitions, which remain visible outside the eligible denominator. Missing,
uncovered, timed-out, runtime-error, pending or unknown outcomes cannot qualify.
Profile review must establish that the native command covers the required mutation
ranges and that any native exclusions are authorized. File and hash checks alone
cannot establish that the declared profile is the complete required production scope.
Native failed tests, including product exceptions, use the existing
[conservative assessment](cis_test_mutation_assess.md); no assertion-text whitelist
is added. Preserve the adopted threshold and any stronger repository policy.

## Results and storage

Outputs live beneath `.cis/local/testing/mutation/<id>/`. `latest-run.json` records
the current attempt even when no readable report exists. `latest-result.json` holds
the latest readable diagnostic or qualification report; `last-qualification.json`
retains the last full passing report. Hash-bound sidecars keep raw counts separate
from execution health and scope. Reconciliation rechecks inputs and the latest
attempt; an older passing report cannot conceal a later failed or running attempt.
Reconciliation also rechecks representative readiness and the actual target path.
`validEvidence`, `thresholdMet` and `qualified` are separate observations: a valid
report at the unchanged threshold can remain unqualified under a readiness hold.
The current representative record is `representative-pilot.json`, with hash-bound
native reports and bounded infrastructure diagnostics beside it; per-attempt
summaries retain costs and original work references.
Governed protection follows the current run summary, stable reports and
representative evidence. After a newer current marker is published, safely settled
superseded run summaries/reports return to the existing retention rules; unresolved
recovery evidence keeps its independent protection.

Successful run directories retain only a small summary; the current native reports
and their sidecars survive separately at the profile level. Failed runs retain bounded
logs and a readable native report when available, for diagnosis until resolved or
superseded. CIS attempts to remove its owned native build/runtime scratch after
execution; a cleanup failure blocks qualification.
Failed runs can also retain up to 32 complete `.txt` or `.log` files from their
native `logs/` directory, within a combined 16 MiB cap. Files are selected in stable
path order without truncation; excess files are omitted. Temporary deployments and
binary build output are removed. Successful native logs remain transient.
It does not clone source, runtime or dependency caches, or create per-mutant proof
bundles. Apply the repository's [retention policy](../references/local-artifact-retention.md)
to remaining transient attempts after writers finish.

The guard bounds wall time, capture, per-run output, the aggregate mutation subtree
and free-space reserve. Storage sampling can overshoot between checks. Dependency
caches and build output outside owned mutation storage are outside its aggregate
byte total for genuinely non-adopted execution. Governed execution additionally
reserves and monitors the declared owner's total roots and exact native scratch,
including declared sibling build outputs; local caps and free-space reserves still
apply. Overlapping paths count once and separate copies count separately. This is
bounded regular-file measurement, not machine-wide or instantaneous filesystem
enforcement. Stops, incomplete capture and cleanup failures hold qualification. Commands
under the same repository are serialized to protect publication and budget checks.

Exit codes: `0` for a completed diagnostic, passing canary or qualification; `2` for
invalid profile/options; `4` for execution, evidence, resource or gate failure.
JSON includes `kind`, `partial`, `qualified`, source/configuration hashes, native
execution, assessment, limits and the summary path. A zero diagnostic exit remains
partial. Follow the [mutation workflow](../workflows/mutation-assurance.md) before
expanding a campaign or declaring release readiness.
Human and agent output also show native validity, threshold and qualification
separately, plus representative readiness, restoration, evidence reference and the
controller's next action, including after a successful canary.

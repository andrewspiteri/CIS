---
title: "cis test mutation run"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-10-26"
review_cadence: "on mutation runner contract change"
cis:
  stable_id: change-impact-studio:manual:cis-test-mutation-run
---

# `cis test mutation run`

Execute a native mutation command through CIS's shared process and storage guard.
Each target supplies a JSON profile; CIS does not contain target-specific assertion
rules, source offsets or test controllers.

```text
cis test mutation run --profile <repository-relative.json> [--kind diagnostic|canary|qualification] [--repo <path>] [--format human|json|agent]
```

The default kind is `diagnostic`. Its native score is visible, but it cannot qualify
the complete scope. `canary` runs the profile's small prerequisite command.
`qualification` requires declared production files and a separate canary command;
it stops if that canary fails before executing the complete profile command.
Qualification applies to the declared profile, not to whole-release readiness.

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
`environment` defaults to empty and supplies target-process variables without
changing the CIS guard's environment. Values are pinned through the profile hash.
For each native command, CIS defaults `TEMP`, `TMP` and `TMPDIR` to a fresh, short
owned directory beneath mutation storage. Its bytes count toward both that command's
run limit and the aggregate retained limit, and CIS removes it after execution.
Explicit profile overrides remain honored. Files outside that command's run and
temporary directories do not count toward its per-run limit; files beneath mutation
storage still count toward the aggregate limit.

```json
{
  "schemaVersion": 1,
  "id": "service-unit",
  "workingDirectory": "src/Service",
  "environment": {"MSBUILDDISABLENODEREUSE": "1", "DOTNET_CLI_USE_MSBUILD_SERVER": "0"},
  "command": ["dotnet", "stryker", "--config-file", "stryker-config.json", "--output", "{output}"],
  "canaryCommand": ["dotnet", "stryker", "--config-file", "canary-config.json", "--output", "{output}"],
  "reportPath": "reports/mutation-report.json",
  "breakThreshold": 90,
  "sourceFiles": ["src/Service/Policy.cs"],
  "configFiles": ["src/Service/Service.csproj", "src/Service/stryker-config.json", "src/Service/canary-config.json", "tests/Service.Tests/PolicyTests.cs"],
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
For isolated .NET campaigns, disable background build-worker reuse as shown above;
otherwise an SDK worker can outlive the native command and trigger the owned-child
cleanup hold. The runner still refuses genuine leftover children.

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
byte total. Stops, incomplete capture and cleanup failures hold qualification. Commands
under the same repository are serialized to protect publication and budget checks.

Exit codes: `0` for a completed diagnostic, passing canary or qualification; `2` for
invalid profile/options; `4` for execution, evidence, resource or gate failure.
JSON includes `kind`, `partial`, `qualified`, source/configuration hashes, native
execution, assessment, limits and the summary path. A zero diagnostic exit remains
partial. Follow the [mutation workflow](../workflows/mutation-assurance.md) before
expanding a campaign or declaring release readiness.

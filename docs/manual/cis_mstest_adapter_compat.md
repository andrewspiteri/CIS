---
title: "Optional MSTest adapter compatibility tool"
type: command-reference
status: Draft
owner: "Andrew Spiteri"
last_reviewed: "2026-10-26"
review_cadence: "on adapter, SDK, rewriter or compatibility runtime change"
cis:
  stable_id: change-impact-studio:manual:mstest-adapter-compat
---

# Optional MSTest adapter compatibility tool

Use this opt-in development tool to prepare a separate compatibility copy of the
exact MSTest.TestAdapter 4.5.1 `net9.0` PlatformServices binary for .NET 10 VSTest
execution. It addresses adapter-owned queue cancellation, completed-result delivery,
worker joins and lifecycle cleanup. The controls below must pass on the consuming
configuration before adoption. It is reusable across test projects and does not
belong in a shipping library or its runtime dependency closure.

The output is an **unofficial modified Microsoft binary**. Its original assembly
identity is retained for adapter binding; its strong-name signature is invalidated.
It is unsupported for .NET Framework, strong-name-enforcing hosts, other adapter
binaries and Microsoft.Testing.Platform execution. The tool rejects a different
input hash or an unexpected method layout; it never edits the original package.

## Prepare one overlay

Build the tool from the reviewed CIS revision. It uses the pinned Mono.Cecil 0.11.6
development dependency. Supply the original adapter DLL from the restored package
and a new or empty owned directory outside that package and all NuGet caches.

```text
dotnet build tools/Cis.MSTestAdapterCompat/Tool -c Release
dotnet tools/Cis.MSTestAdapterCompat/Tool/bin/Release/net10.0/Cis.MSTestAdapterCompat.Tool.dll --input <original-net9.0/MSTestAdapter.PlatformServices.dll> --output-directory <empty-owned-directory>
dotnet tools/Cis.MSTestAdapterCompat/Tool/bin/Release/net10.0/Cis.MSTestAdapterCompat.Tool.dll --verify-overlay <owned-directory>
```

Creation writes three files: the modified adapter, its small CIS compatibility
runtime and `compatibility-manifest.json`. The manifest identifies the original
package, tool, rewrite policy, dependency and output hashes. Existing output is
never overwritten. Verification also binds the runtime to the current tool build;
editing a manifest does not establish trust in an adapter. Review and pin the
generated binary hashes in the consuming profile.

## Enable it for a bounded native run

Set these target-process environment variables in the repository-owned
[`cis test mutation run`](cis_test_mutation_run.md) profile. Use absolute paths.

| Variable | Value |
|---|---|
| `CustomAfterMicrosoftCommonTargets` | The reviewed `tools/Cis.MSTestAdapterCompat/Cis.MSTestAdapterCompat.targets` file |
| `CisMSTestCompatOverlay` | The prepared overlay directory |
| `CisMSTestCompatTool` | The matching built tool DLL |
| `CisMSTestCompatDotnetHost` | The existing absolute .NET host executable |
| `CisMSTestCompatAdapterSha256` | Reviewed adapter SHA256 from the manifest |
| `CisMSTestCompatRuntimeSha256` | Reviewed runtime SHA256 from the manifest |

The target also accepts the SDK's `DotNetHostPath` or `DOTNET_HOST_PATH` when the
explicit host is absent; it never searches PATH. Explicit use requires a `net10.0`
test project with the exact supported adapter in its output. Each build verifies
the overlay and hashes before copying its three files to that test output.

Include the tool, runtime, targets, overlay, native runner configuration and relevant
tests in `configFiles`. Hashing establishes input identity, not dependency approval
or runner correctness. Use finite time and storage limits and keep successful
diagnostics transient under the [mutation workflow](../workflows/mutation-assurance.md).

## Validate and maintain

Run the automated compatibility tests and a small native mutation control before
adopting an overlay. Check the selected test identities, real failed and passing
results, queued work, joins, cleanup callbacks and native termination. Include
cancellation during the final active test and a genuine cleanup failure. A raw
mutation kill or a zero process exit alone does not establish execution health.

Restore the fixture's test project before native analysis, especially after removing
its transient `obj/` directory: `dotnet restore tests/fixtures/MSTestAdapterCompat/Tests/Tests.csproj`.

The neutral fixture under `tests/fixtures/MSTestAdapterCompat` exercises these
paths. Its optional build properties enable method parallelism, dependency ordering
and an assembly without an initialization hook. Fixture capture is temporary
diagnostic support; it neither interprets outcomes nor qualifies a campaign.
The fixture pins MSTest 4.5.1, Microsoft.NET.Test.Sdk 18.10.1, runtime 10.0.12
and Stryker.NET 4.16.0. These pins describe the reference controls, not support
for every .NET 10 or VSTest build. Validate the actual consuming host configuration.

Compatibility tests and small native controls cover their executed configurations.
They do not qualify a broad campaign, change native outcomes, award timeout credit
or relax the adopted threshold. External cancellation and genuine test, cleanup or
infrastructure failures remain failures.

Keep this compatibility tool temporary. When upgrading MSTest or the host, first
test the official binaries against the same regressions. Remove the opt-in variables
and clean/rebuild test output once the official path passes. Retire the overlay and
resolved diagnostic captures. A changed upstream binary requires a separately
reviewed compatibility update; never broaden the accepted hash automatically.

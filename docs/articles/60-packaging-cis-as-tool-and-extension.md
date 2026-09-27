---
title: "Packaging CIS for Windows, Linux and VS Code"
type: article
status: Active
series: "Building Change Impact Studio"
series_order: 10
owner: "Andrew Spiteri"
last_reviewed: "2026-09-27"
review_cadence: on release change
summary: "One semantic version for standalone Windows and Linux executables, the .NET tool and VS Code extension, with package smoke tests and checksums."
cis:
  stable_id: change-impact-studio:article:packaging-cis
---

# Packaging CIS for Windows, Linux and VS Code

Source tests do not prove that users receive a complete product. Packaging is its own
delivery boundary.

## One version

`Version.props` is the authoritative semantic version and must match the VS Code
extension package. Release tags use `v<VersionPrefix>`.

## Build the complete product

The release script restores, builds, and tests the solution; checks extension syntax and
tests; packs the .NET tool and VSIX; builds standalone Windows x64 and Linux x64
executables; and creates a tracked-source archive.

## Run without installing .NET

The standalone archives contain one executable each: `cis.exe` for Windows and `cis`
for Linux. They bundle the .NET runtime. The Linux archive targets glibc-based x64
systems and preserves the executable permission; it does not target Alpine/musl.
Git and external tools required by a chosen workflow remain separate prerequisites.

`tools/build-standalone.ps1` can build either platform or both. CI builds and tests
each archive on its native operating system before allowing release packaging.
The smoke test runs the extracted executable alone in an isolated directory with
the .NET runtime lookup redirected away from the installed SDK. It checks module
registration, embedded repository starters, strict validators, SQLite graph storage
and C# binding to runtime metadata.

The application extracts bundled libraries into a per-user cache at runtime so
SQLite and compiler analysis can load the files they need. These builds use neither
trimming nor Native AOT. The [installation guide](../../README.md#21-installing-cis)
explains cache configuration and current download availability. The older v0.3.0
release does not contain standalone archives.

## Smoke-test the installed tool

The generated NuGet package is installed into an isolated path with an isolated package
cache. Help and representative module commands confirm that explicit registration and
package contents work outside the source tree.

The VSIX smoke boundary includes clean-profile installation, CLI compatibility, activation
against initialized and uninitialized repositories, the getting-started path, and Repository
Doctor. Source-level extension tests alone do not prove that the packaged client can locate
and safely coordinate the packaged CLI.

## Publish verifiable artifacts

The bundle includes the Windows ZIP, Linux tar.gz, tool package, VSIX, source archive,
and `SHA256SUMS`. The release
workflow validates tag/version alignment and uploads the complete set.

## A release is a coordinated contract

The standalone executables, .NET tool, VSIX, manuals, source archive, checksums, and release notes describe one
product version. Mismatched versions make compatibility and support ambiguous even when
each artifact builds independently.

`Version.props` remains the source of version authority. Build scripts validate dependent
package metadata and the release tag rather than allowing clients to drift.

## Build from a known source state

Release evidence should identify the commit, SDK and Node versions, dependency lock or
package state, build arguments, and workflow run. Dirty or locally patched inputs must not
be presented as the tagged source archive.

Reproducibility does not require byte-identical output on every platform unless the policy
claims it. It does require enough identity to explain and repeat the release process.

## Test what users install

After packing, install the NuGet tool into an isolated directory and package cache. Run
help, module inventory, strict documentation or repository diagnostics, and representative
commands outside the source tree. This catches missing content, assembly-loading errors,
and accidental dependencies on the development layout.

Install the VSIX in a clean profile and verify activation, CLI discovery, version
compatibility, getting started, multi-root authority behavior, and representative views.
The client should fail clearly when the CLI is absent or incompatible.

## Verify the bundle

Generate checksums after final artifacts exist and validate them before publication. The
bundle should contain exactly the declared packages, source archive, and checksum file.
Release notes identify outcomes, compatibility changes, migrations, known limits, and
verification rather than listing commits alone.

## Keep release separate from deployment

Publishing a valid bundle does not prove that every user upgraded, every environment
deployed, or every external provider remains compatible. Those concerns belong to their
deployment and operational authority. The release record states what was packaged and
verified.

## Preserve failure and rollback

A failed smoke test blocks publication even if source suites passed. Partial uploads,
checksum mismatches, and tag/version disagreement require repair or a new governed release;
they should not be fixed by replacing an artifact under the same immutable version.

## Audit release completion

Before publication, confirm source revision, version alignment, clean builds, solution and
extension tests, both standalone archives, tool and VSIX package contents, isolated installations, representative
commands, clean-profile editor behavior, source archive, checksums, release notes, and
known limitations. Record unavailable evidence explicitly.

After publication, install from the released location rather than the local build folder
and repeat a minimal smoke check. This final step catches upload or bundle mistakes that
the build workspace cannot see.

## Takeaway

Treat packaging as product behavior. Align versions, verify both operating systems
and the editor client, test the actual packages, inspect registered capabilities,
and publish checksums with the release.

## Canonical CIS sources

- [Versioning and release](../standards/versioning-and-release.md)
- [Delivery and assurance](../specs/delivery-and-assurance-spec.md)
- [Visual Studio Code client](../specs/vscode-client-spec.md)

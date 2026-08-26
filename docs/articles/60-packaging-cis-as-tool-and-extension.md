---
title: "Packaging CIS as a .NET Tool and VS Code Extension"
type: article
status: Draft
series: "Building Change Impact Studio"
series_order: 10
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: on release change
summary: "One semantic version, verified source and client builds, packaged-tool smoke tests, archives, and checksums."
cis:
  stable_id: change-impact-studio:article:packaging-cis
---

# Packaging CIS as a .NET tool and VS Code extension

Source tests do not prove that users receive a complete product. Packaging is its own
delivery boundary.

## One version

`Version.props` is the authoritative semantic version and must match the VS Code
extension package. Release tags use `v<VersionPrefix>`.

## Build the complete product

The release script restores, builds, and tests the solution; checks extension syntax and
tests; packs the .NET tool and VSIX; and creates a tracked-source archive.

## Smoke-test the installed tool

The generated NuGet package is installed into an isolated path with an isolated package
cache. Help and representative module commands confirm that explicit registration and
package contents work outside the source tree.

## Publish verifiable artifacts

The bundle includes the tool package, VSIX, source archive, and `SHA256SUMS`. The release
workflow validates tag/version alignment and uploads the complete set.

## Takeaway

Treat packaging as product behavior. Align versions, verify both clients, install the
actual package, inspect registered capabilities, and publish checksums with the release.

## Canonical CIS sources

- [Versioning and release](../standards/versioning-and-release.md)
- [Delivery and assurance](../specs/delivery-and-assurance-spec.md)
- [Visual Studio Code client](../specs/vscode-client-spec.md)

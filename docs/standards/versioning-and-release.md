---
title: "Versioning and release policy"
type: standard
status: Active
targets:
  - repository-governance
  - release
owner: "Andrew Spiteri"
last_reviewed: "2026-08-23"
review_cadence: "on release workflow change"
source_of_truth: This file
cis:
  stable_id: change-impact-studio:standard:versioning-release
---

# Versioning and release policy

## Purpose

Keep every CIS distribution on one auditable version and prevent publication of an incomplete or unverified CLI, extension, or source archive.

## Scope

Applies to the CIS .NET tool, VS Code extension, source archive, CI workflows, release tags, SDK selection, and centrally managed dependencies.

## Normative language

`MUST` and `MUST NOT` are mandatory. `SHOULD` requires recorded rationale when not followed. `MAY` is optional.

## Version model

CIS uses Semantic Versioning. The single product version is declared in `Version.props` and must match the VS Code extension `package.json` version.

- Patch: compatible fixes, documentation, and internal hardening.
- Minor: compatible commands, options, modules, schemas with migration support, or extension capabilities.
- Major: breaking CLI, canonical Markdown, workspace, graph, provider, or extension contracts.

The SDK remains pinned by `global.json`; dependency updates remain centrally pinned in `Directory.Packages.props`. A release tag must be exactly `v<VersionPrefix>`.

Every release must pass the Windows and Linux CI matrix. The release build produces a .NET tool package, a VSIX, a tracked-source archive, and SHA-256 checksums; it also installs the generated tool package and runs its help command before publication.

## Rules

- **REL-001** `Version.props` MUST be the authoritative product version and the VS Code extension version MUST match it.
- **REL-002** Version changes MUST follow Semantic Versioning according to the compatibility impact described above.
- **REL-003** The SDK MUST remain pinned in `global.json`, and centrally managed dependencies MUST remain pinned in `Directory.Packages.props`.
- **REL-004** A release tag MUST equal `v<VersionPrefix>` and MUST pass the supported Windows and Linux verification matrix.
- **REL-005** A release MUST contain the .NET tool, VSIX, tracked-source archive, and SHA-256 checksums, and MUST smoke-test the packaged CLI including its registered built-in modules before publication.

## Verification

- `REL-001`: Compare `Version.props` with `vscode-extension/package.json`; the release script rejects divergence.
- `REL-002`: Review the proposed version against the documented patch, minor, and major compatibility rules.
- `REL-003`: Validate `global.json` and `Directory.Packages.props` in CI and during dependency review.
- `REL-004`: Run the CI matrix and release tag/version validation workflow.
- `REL-005`: Run `tools/build-release.ps1`, verify `SHA256SUMS`, install the generated tool into a clean path, and inspect `cis host modules` plus representative module commands.

## Exceptions

An exception requires the affected rule ID, human approver, rationale, bounded scope, review or expiry condition, and compensating controls. CIS and agents cannot approve exceptions.

---
title: "Versioning and release policy"
type: standard
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-23"
review_cadence: "on release workflow change"
cis:
  stable_id: change-impact-studio:standard:versioning-release
---

# Versioning and release policy

CIS uses Semantic Versioning. The single product version is declared in `Version.props` and must match the VS Code extension `package.json` version.

- Patch: compatible fixes, documentation, and internal hardening.
- Minor: compatible commands, options, modules, schemas with migration support, or extension capabilities.
- Major: breaking CLI, canonical Markdown, workspace, graph, provider, or extension contracts.

The SDK remains pinned by `global.json`; dependency updates remain centrally pinned in `Directory.Packages.props`. A release tag must be exactly `v<VersionPrefix>`.

Every release must pass the Windows and Linux CI matrix. The release build produces a .NET tool package, a VSIX, a tracked-source archive, and SHA-256 checksums; it also installs the generated tool package and runs its help command before publication.

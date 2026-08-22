---
title: "Implementation Skill Packs"
type: implementation-skill-pack-profile
status: Active
owner: "Repository maintainers"
last_reviewed: "2026-08-15"
review_cadence: "on repository classification change"
cis:
  stable_id: change-impact-studio:reference:implementation-skill-packs
---

# Implementation skill packs

This profile records the implementation skills selected by `cis repo init` from confirmed repository classification evidence. Markdown skills under `.github/skills/` remain reviewable guidance; they do not grant tools, credentials, or approval authority.

| Pack | Selection reason | Seeded skills |
| --- | --- | --- |
| `core-testing` | Production implementation behavior was detected. | `cis-add-unit-tests`, `cis-add-architecture-tests`, `cis-add-mutation-tests` |
| `dependency-and-release` | Package, build, or deployment metadata was detected. | `cis-dependency-upgrades`, `cis-release-rollout` |
| `dotnet-quality` | C# implementation was detected. | `cis-dotnet-test` |

## Classification evidence

- `core-testing`: cis-abstractions: src/Cis.Abstractions/Cis.Abstractions.csproj
- `core-testing`: cis-host: src/Cis.Host/Cis.Host.csproj
- `core-testing`: cis-modules-agent: src/Cis.Modules.Agent/Cis.Modules.Agent.csproj
- `core-testing`: cis-modules-ai: src/Cis.Modules.Ai/Cis.Modules.Ai.csproj
- `core-testing`: cis-modules-api: src/Cis.Modules.Api/Cis.Modules.Api.csproj
- `core-testing`: cis-modules-brd: src/Cis.Modules.Brd/Cis.Modules.Brd.csproj
- `core-testing`: cis-modules-change: src/Cis.Modules.Change/Cis.Modules.Change.csproj
- `core-testing`: cis-modules-context: src/Cis.Modules.Context/Cis.Modules.Context.csproj
- `core-testing`: cis-modules-decision: src/Cis.Modules.Decision/Cis.Modules.Decision.csproj
- `core-testing`: cis-modules-design: src/Cis.Modules.Design/Cis.Modules.Design.csproj
- `core-testing`: cis-modules-diagnostics: src/Cis.Modules.Diagnostics/Cis.Modules.Diagnostics.csproj
- `core-testing`: cis-modules-docs: src/Cis.Modules.Docs/Cis.Modules.Docs.csproj
- `dependency-and-release`: cis-host: src/Cis.Host/Cis.Host.csproj
- `dependency-and-release`: change-impact-studio: vscode-extension/package.json
- `dependency-and-release`: change-impact-studio: vscode-extension package dependency
- `dotnet-quality`: cis-abstractions: src/Cis.Abstractions/Cis.Abstractions.csproj
- `dotnet-quality`: cis-host: src/Cis.Host/Cis.Host.csproj
- `dotnet-quality`: cis-modules-agent: src/Cis.Modules.Agent/Cis.Modules.Agent.csproj
- `dotnet-quality`: cis-modules-ai: src/Cis.Modules.Ai/Cis.Modules.Ai.csproj
- `dotnet-quality`: cis-modules-api: src/Cis.Modules.Api/Cis.Modules.Api.csproj
- `dotnet-quality`: cis-modules-brd: src/Cis.Modules.Brd/Cis.Modules.Brd.csproj
- `dotnet-quality`: cis-modules-change: src/Cis.Modules.Change/Cis.Modules.Change.csproj
- `dotnet-quality`: cis-modules-context: src/Cis.Modules.Context/Cis.Modules.Context.csproj
- `dotnet-quality`: cis-modules-decision: src/Cis.Modules.Decision/Cis.Modules.Decision.csproj
- `dotnet-quality`: cis-modules-design: src/Cis.Modules.Design/Cis.Modules.Design.csproj
- `dotnet-quality`: cis-modules-diagnostics: src/Cis.Modules.Diagnostics/Cis.Modules.Diagnostics.csproj
- `dotnet-quality`: cis-modules-docs: src/Cis.Modules.Docs/Cis.Modules.Docs.csproj

## Reconciliation

Rerun `cis repo init --root <documentation-root> --dry-run` after component, language, framework, role, or capability changes. Review the plan, then apply with `--yes`. Run `cis skills validate --strict` after reconciliation.

---
title: "change-impact-studio Delivery and Assurance"
type: specification
status: Draft
owner: Repository maintainer
review_cadence: on delivery-policy change
cis:
  stable_id: change-impact-studio:spec:delivery-and-assurance
---

# change-impact-studio Delivery and Assurance

## Human control

- A human owns scope, risk acceptance, canonical decisions, and release approval.
- Agents may propose and implement bounded changes but must surface ambiguity, collisions, and unverified assumptions.
- A current explicit feature approval may be reused by deterministic `cis plan derive`
  for eligible impacts and the exact validated plan. This carry-forward preserves the
  original reviewer, rationale, and digest and stops on uncertainty; it is not a new
  agent approval.
- UI work uses one default review of the exact wireframe digest and rendered design pack.
  An earlier standalone wireframe checkpoint is optional; final acceptance remains an
  explicit human action.

## Documentation obligations

- Update affected specifications and living references in the same change as behavior.
- Preserve stable IDs and catalog entries. Do not silently delete or replace canonical records.

## Deterministic verification

- `cis-abstractions` (`src/Cis.Abstractions`): validate the affected csharp, dotnet surface.
- `cis-host` (`src/Cis.Host`): validate the affected csharp, dotnet surface.
- `cis-modules-ai` (`src/Cis.Modules.Ai`): validate the affected csharp, dotnet surface.
- `cis-modules-brd` (`src/Cis.Modules.Brd`): validate the affected csharp, dotnet surface.
- `cis-modules-change` (`src/Cis.Modules.Change`): validate the affected csharp, dotnet surface.
- `cis-modules-context` (`src/Cis.Modules.Context`): validate the affected csharp, dotnet surface.
- `cis-modules-decision` (`src/Cis.Modules.Decision`): validate the affected csharp, dotnet surface.
- `cis-modules-design` (`src/Cis.Modules.Design`): validate the affected csharp, dotnet surface.
- `cis-modules-docs` (`src/Cis.Modules.Docs`): validate the affected csharp, dotnet surface.
- `cis-modules-feedback` (`src/Cis.Modules.Feedback`): validate the affected csharp, dotnet surface.
- `cis-modules-graph` (`src/Cis.Modules.Graph`): validate the affected csharp, dotnet surface.
- `cis-modules-host` (`src/Cis.Modules.Host`): validate the affected csharp, dotnet surface.
- `cis-modules-impact` (`src/Cis.Modules.Impact`): validate the affected csharp, dotnet surface.
- `cis-modules-index` (`src/Cis.Modules.Index`): validate the affected csharp, dotnet surface.
- `cis-modules-plan` (`src/Cis.Modules.Plan`): validate the affected csharp, dotnet surface.
- `cis-modules-repository` (`src/Cis.Modules.Repository`): validate the affected csharp, dotnet surface.
- `cis-host-tests` (`tests/Cis.Host.Tests`): validate the affected csharp, dotnet-test surface.
- `cis-modules-brd-tests` (`tests/Cis.Modules.Brd.Tests`): validate the affected csharp, dotnet-test surface.
- `cis-modules-context-tests` (`tests/Cis.Modules.Context.Tests`): validate the affected csharp, dotnet-test surface.
- `cis-modules-delivery-tests` (`tests/Cis.Modules.Delivery.Tests`): validate the affected csharp, dotnet-test surface.
- `cis-modules-docs-tests` (`tests/Cis.Modules.Docs.Tests`): validate the affected csharp, dotnet-test surface.
- `cis-modules-feedback-tests` (`tests/Cis.Modules.Feedback.Tests`): validate the affected csharp, dotnet-test surface.
- `cis-modules-graph-tests` (`tests/Cis.Modules.Graph.Tests`): validate the affected csharp, dotnet-test surface.
- `cis-modules-index-tests` (`tests/Cis.Modules.Index.Tests`): validate the affected csharp, dotnet-test surface.
- `cis-modules-repository-tests` (`tests/Cis.Modules.Repository.Tests`): validate the affected csharp, dotnet-test surface.

## Independent assurance

- TODO: Define the required reviewer, automated gate, or independent check for each risk class.

## Completion evidence

- Record commands, results, residual risks, and any checks that could not be run.

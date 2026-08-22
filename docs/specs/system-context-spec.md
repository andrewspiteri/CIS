---
title: "change-impact-studio System Context"
type: specification
status: Draft
owner: Repository maintainer
review_cadence: on architecture change
cis:
  stable_id: change-impact-studio:spec:system-context
---

# change-impact-studio System Context

## Purpose and outcomes

TODO: Describe the user or business outcome this repository supports.

## Boundary

TODO: State what is inside this system boundary and what is explicitly outside it.

## Detected components

Repository shape: **monorepo**.

| Component | Root | Detected roles | Detected capabilities |
|---|---|---|---|
| cis-abstractions | `src/Cis.Abstractions` | shared-library | configuration |
| cis-host | `src/Cis.Host` | tooling | configuration |
| cis-modules-ai | `src/Cis.Modules.Ai` | shared-library | configuration |
| cis-modules-brd | `src/Cis.Modules.Brd` | shared-library | configuration |
| cis-modules-change | `src/Cis.Modules.Change` | shared-library | configuration |
| cis-modules-context | `src/Cis.Modules.Context` | shared-library | configuration |
| cis-modules-decision | `src/Cis.Modules.Decision` | shared-library | configuration |
| cis-modules-design | `src/Cis.Modules.Design` | shared-library | configuration |
| cis-modules-docs | `src/Cis.Modules.Docs` | shared-library | configuration |
| cis-modules-feedback | `src/Cis.Modules.Feedback` | shared-library | configuration |
| cis-modules-graph | `src/Cis.Modules.Graph` | shared-library | configuration |
| cis-modules-host | `src/Cis.Modules.Host` | shared-library | configuration |
| cis-modules-impact | `src/Cis.Modules.Impact` | shared-library | configuration |
| cis-modules-index | `src/Cis.Modules.Index` | shared-library | configuration |
| cis-modules-plan | `src/Cis.Modules.Plan` | shared-library | configuration |
| cis-modules-repository | `src/Cis.Modules.Repository` | shared-library | configuration |
| cis-host-tests | `tests/Cis.Host.Tests` | test-automation | configuration |
| cis-modules-brd-tests | `tests/Cis.Modules.Brd.Tests` | test-automation | configuration |
| cis-modules-context-tests | `tests/Cis.Modules.Context.Tests` | test-automation | configuration |
| cis-modules-delivery-tests | `tests/Cis.Modules.Delivery.Tests` | test-automation | configuration |
| cis-modules-docs-tests | `tests/Cis.Modules.Docs.Tests` | test-automation | configuration |
| cis-modules-feedback-tests | `tests/Cis.Modules.Feedback.Tests` | test-automation | configuration |
| cis-modules-graph-tests | `tests/Cis.Modules.Graph.Tests` | test-automation | configuration |
| cis-modules-index-tests | `tests/Cis.Modules.Index.Tests` | test-automation | configuration |
| cis-modules-repository-tests | `tests/Cis.Modules.Repository.Tests` | test-automation | configuration |

## Actors and external systems

| Actor or system | Relationship | Contract or reference | Owner |
|---|---|---|---|
| TODO | TODO | TODO | TODO |

## Constraints and invariants

- TODO: Record material technical, regulatory, operational, and compatibility constraints.

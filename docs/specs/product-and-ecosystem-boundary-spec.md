---
title: "Product and Ecosystem Boundary"
type: specification
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-05"
review_cadence: "on workspace-boundary change"
cis:
  stable_id: change-impact-studio:spec:product-and-ecosystem-boundary
---

# Product and ecosystem boundary

## Purpose

CIS separates a governable product from the wider software ecosystem in which it
operates. A banking ecosystem may contain Cards, Transfers, AML, Core Banking, and
other products without treating every imported repository as owned implementation for
one change authority.

## Authority model

One CIS workspace governs exactly one product and records exactly one ecosystem. A
different product has a separate workspace, BRD, technical intent, backlog, change
authority, and approval chain even when both products use the same ecosystem ID.

The workspace registry is schema version 2. Legacy workspace registries are rejected;
CIS does not infer product ownership from an earlier unqualified participant list.

## Repository participation

Every registered repository has both a structural role and product participation:

| Field | Values | Meaning |
| --- | --- | --- |
| `role` | `authority`, `participant` | Identifies the one documentation authority; all other repositories are participants. |
| `participation` | `owned`, `dependency` | States whether implementation is governed by this product workspace. |
| `relationship` | `none`, `producer`, `consumer`, `bidirectional` | States dependency direction relative to the governed product. |
| `components` | zero or more stable IDs | Limits dependency evidence to relevant components when the repository contains a broader system. |

The authority is always `owned` with relationship `none`. Every other owned repository
also uses relationship `none`. A dependency must declare `producer`, `consumer`, or
`bidirectional`; it cannot become the authority.

`producer` means that the imported dependency provides capabilities, events, data, or
contracts consumed by this product. `consumer` means that it consumes capabilities,
events, data, or contracts provided by this product. `bidirectional` means both are
true. These directions describe integration, not ownership.

## Governance effects

- BRD and feature-specification discovery considers only product-owned repositories.
  Documents in dependency repositories belong to another authority and do not silently
  become product requirements.
- The canonical technical intent records dependency surfaces and directional
  integration points, but it does not infer a dependency's framework, database, or
  architecture as the product's implementation direction.
- Backlog and detailed-plan implementation targets are limited to owned delivery
  repositories. A dependency change requires a separately governed change in the
  dependency's owning product workspace.
- Agents may read a dependency as bounded context. Workspace-write execution against a
  dependency is rejected.
- Workspace graph build and list output expose ecosystem, product, participation,
  relationship, and component scope. Per-repository Markdown and graphs remain
  canonical; a unified cross-repository compiler graph is outside this boundary.

## Reconciliation

Import is idempotent. Re-importing a repository with the same boundary is unchanged.
Re-importing it with a different participation, relationship, or component scope
reclassifies the existing registry entry without duplicating it. CIS never infers this
material ownership change; the caller must provide it explicitly.

Removing a repository from the workspace remains an explicit future operation. Omitting
it from a later import does not remove it.

## Canonical example

```yaml
schema_version: 2
ecosystem:
  id: retail-banking
  name: Retail Banking
product:
  id: cards
  name: Cards
repositories:
  - id: cards-docs
    path: .
    documentation_root: docs/cis
    role: authority
    participation: owned
    relationship: none
    components: []
  - id: cards-api
    path: ../cards-api
    documentation_root: docs/cis
    role: participant
    participation: owned
    relationship: none
    components: []
  - id: core-banking
    path: ../core-banking
    documentation_root: docs/cis
    role: participant
    participation: dependency
    relationship: producer
    components:
      - accounts-api
      - customer-events
```

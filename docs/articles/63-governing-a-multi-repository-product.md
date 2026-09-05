---
title: "Governing a Multi-Repository Product"
type: article
status: Draft
series: "CIS in Practice"
series_order: 3
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: on workspace-governance change
summary: "Establish one product authority while distinguishing owned repositories from producer and consumer dependencies."
cis:
  stable_id: change-impact-studio:article:govern-multi-repository-product
---

# Governing a multi-repository product

A product spanning web, services, mobile, and infrastructure needs shared intent without
turning one repository into a stale copy of all the others.

## Initialize the authority

```powershell
cis workspace init --root docs --ecosystem commerce --product ordering --dry-run
cis workspace init --root docs --ecosystem commerce --product ordering --yes
```

The authority owns workspace business requirements, technical intent, and cross-repository
change dossiers.

## Import participants

```powershell
cis repo import `
  --source C:\work\web C:\work\api C:\work\infra `
  --root docs\cis `
  --participation owned --relationship none `
  --dry-run
```

Import initializes and registers locations. It does not copy source. A repository owned
by another product is imported separately as a `dependency` with an explicit `producer`,
`consumer`, or `bidirectional` relationship and optional component scope.

## Build and validate independently

```powershell
cis graph build --workspace C:\work\product-docs
cis graph validate --workspace C:\work\product-docs --strict
```

Each repository retains its own status and graph identity. Partial failure remains visible.

## Govern shared intent

Business requirements assess product-owned source documents and baselines. Technical
intent establishes ownership, trust, integration, data, and operational direction while
recording dependency surfaces without absorbing their implementation choices. Material
owned-product drift blocks new governed workspace work until reviewed.

## Takeaway

Use one authority for one product and reuse the ecosystem identity across related product
workspaces. Federate repository evidence while preserving repository-qualified identity,
independent health, and ownership.

## Canonical CIS sources

- [`cis workspace init`](../manual/cis_workspace_init.md)
- [`cis repo import`](../manual/cis_repo_import.md)
- [Business requirements governance](../specs/business-requirements-governance-spec.md)
- [Technical-intent governance](../specs/technical-intent-governance-spec.md)

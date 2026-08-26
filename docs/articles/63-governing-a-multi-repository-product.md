---
title: "Governing a Multi-Repository Product"
type: article
status: Draft
series: "CIS in Practice"
series_order: 3
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: on workspace-governance change
summary: "Establish one cross-product authority while preserving participant repository facts, baselines, and ownership."
cis:
  stable_id: change-impact-studio:article:govern-multi-repository-product
---

# Governing a multi-repository product

A product spanning web, services, mobile, and infrastructure needs shared intent without
turning one repository into a stale copy of all the others.

## Initialize the authority

```powershell
cis workspace init --root docs --dry-run
cis workspace init --root docs --yes
```

The authority owns workspace business requirements, technical intent, and cross-repository
change dossiers.

## Import participants

```powershell
cis repo import `
  --source C:\work\web C:\work\api C:\work\infra `
  --root docs\cis `
  --dry-run
```

Import initializes and registers locations. It does not copy source or infer cross-repository
relationships.

## Build and validate independently

```powershell
cis graph build --workspace C:\work\product-docs
cis graph validate --workspace C:\work\product-docs --strict
```

Each participant retains its own status and graph identity. Partial failure remains visible.

## Govern shared intent

Business requirements assess source documents and participant baselines. Technical intent
establishes ownership, trust, integration, data, and operational direction. Material drift
blocks new governed workspace work until reviewed.

## Takeaway

Use one authority for cross-product meaning and federate participant evidence. Preserve
repository-qualified identity, independent health, and local ownership.

## Canonical CIS sources

- [`cis workspace init`](../manual/cis_workspace_init.md)
- [`cis repo import`](../manual/cis_repo_import.md)
- [Business requirements governance](../specs/business-requirements-governance-spec.md)
- [Technical-intent governance](../specs/technical-intent-governance-spec.md)


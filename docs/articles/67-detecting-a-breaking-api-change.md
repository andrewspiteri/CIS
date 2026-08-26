---
title: "Detecting a Breaking API Change"
type: article
status: Draft
series: "CIS in Practice"
series_order: 7
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: on API-diff change
summary: "Discover current operations, validate governance fields, compare every supported baseline, then route the break to a human decision."
cis:
  stable_id: change-impact-studio:article:detect-breaking-api-change
---

# Detecting a breaking API change

A developer changes a response property from optional to required. The newest consumer
already supports it, and the current OpenAPI diff appears manageable. An older supported
client does not.

## Establish current inventory

```powershell
cis api discover
cis api inventory
cis api validate
```

The inventory combines implementation, OpenAPI, ownership, consumers, exposure,
authentication, errors, cache behavior, and lifecycle.

## Compare the support window

```powershell
cis api diff
```

The default comparison evaluates the current contract against every configured supported
baseline. Compatibility with only the newest baseline is insufficient.

## Treat the break as impact

Affected consumers, documentation, SDKs, rollout, monitoring, and deprecation policy
become impact findings. The tool reports evidence; it does not choose versioning or accept
the break.

## Resolve the strategy

A human selects preservation, additive transition, new version, consumer migration, or
an explicitly governed exception with rationale.

## Takeaway

Breaking-change detection is a pipeline from source and inventory through all supported
baselines to consumer impact and human compatibility policy.

## Canonical CIS sources

- [API design and governance](../specs/api-design-and-governance-spec.md)
- [API governance profile](../references/api-governance-profile.md)
- [`cis api diff`](../manual/cis_api_diff.md)


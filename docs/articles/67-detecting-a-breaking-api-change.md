---
title: "Detecting a Breaking API Change"
type: article
status: Active
series: "CIS in Practice"
series_order: 7
owner: "Andrew Spiteri"
last_reviewed: "2026-09-10"
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
the break. When the public contract depends on an external specification, reference
inventory and drift evidence belongs in the same impact assessment.

## Resolve the strategy

A human selects preservation, additive transition, new version, consumer migration, or
an explicitly governed exception with rationale.

## Define the support promise

Detection needs a governed answer to “breaking for whom?” The API profile identifies the
supported baselines, consumers, lifecycle, and comparison policy. A development artifact
or newest client is not automatically the oldest contract the product still promises.

The inventory also records behavior OpenAPI cannot fully express: ownership, exposure,
authentication, problems, caching, dynamic provider surfaces, and evidence.

## Work through a nullability change

Suppose `displayName` was optional in versions 1 and 2 and becomes required in the current
schema. A comparator can identify the structural difference. Review still asks:

- can old records produce null at runtime?
- do strict clients reject the new required declaration?
- did generated SDK types change incompatibly?
- which supported consumers remain on each baseline?
- is a data migration required before publication?
- what error or fallback appears during mixed-version rollout?

Compatibility is the combined product behavior, not the diff label alone.

## Look beyond removals

Renames, required fields, narrower types, new enum members, changed defaults, status codes,
problem contracts, ordering, pagination, headers, authentication, cache semantics, and side
effects can all break consumers. Additive schema change is not automatically safe for
exhaustive or strict clients.

Runtime and consumer evidence can reveal concerns absent from generated schemas. Preserve
those findings without weakening the deterministic comparison.

## Trace consumers across authority boundaries

Owned consumers can receive migration tasks in the current workspace. Dependency
consumers belong to another product and create coordination findings. Record the contract,
consumer version, owner, migration need, and communication path rather than assuming an
imported repository is writable.

## Choose and govern the strategy

| Strategy | Typical obligation |
|---|---|
| Preserve current contract | Adapt implementation and data while keeping consumer behavior |
| Additive transition | Support old and new forms with a measured migration window |
| New version | Publish parallel contract, route consumers, and define retirement |
| Coordinated break | Obtain authority, migrate every affected consumer, and control rollout |
| Bounded exception | Record exact scope, risk, compensating controls, and expiry |

The reviewer selects one with evidence and rationale before implementation makes it
irreversible.

## Verify migration and release

Compare every supported baseline again, test representative consumers and generated SDKs,
validate data states, exercise rollback, and monitor errors and version usage. Release
notes state the changed operations, action required, dates, and known limits.

## Keep the evidence sources reconciled

Source discovery, the governed API inventory, generated OpenAPI, supported baseline
artifacts, SDKs, and observed consumers can drift independently. Validation should identify
which source is stale or contradictory. Regenerating OpenAPI does not authorize a changed
inventory lifecycle, and editing the inventory does not prove the implementation follows
it. Compatibility review is complete only when the current identities and digests agree
or every discrepancy has a disposition.

## Takeaway

Breaking-change detection is a pipeline from source and inventory through all supported
baselines to consumer impact and human compatibility policy.

## Canonical CIS sources

- [API design and governance](../specs/api-design-and-governance-spec.md)
- [API governance profile](../references/api-governance-profile.md)
- [`cis api diff`](../manual/cis_api_diff.md)
- [Reference governance and drift](../specs/reference-governance-and-drift-spec.md)

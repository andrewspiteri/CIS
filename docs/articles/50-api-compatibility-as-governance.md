---
title: "API Compatibility as a Governance Problem"
type: article
status: Draft
series: "Standards and Engineering Policy"
series_order: 9
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: on API-governance change
summary: "Compatibility depends on supported baselines, consumers, lifecycle, ownership, and human policy—not only a schema diff."
cis:
  stable_id: change-impact-studio:article:api-compatibility-governance
---

# API compatibility as a governance problem

An OpenAPI diff can identify structural change. It cannot decide which baselines must be
supported, who owns the break, whether consumers have migrated, or which exception is
acceptable.

## Inventory the governed contract

CIS relates implemented routes, an API dictionary, OpenAPI operations, ownership,
consumers, exposure, authentication, cache behavior, errors, lifecycle, and evidence.

## Compare every supported baseline

Compatibility is forward-transitive across the configured support window. A current
contract should not be considered safe merely because it is compatible with the newest
baseline while breaking an older still-supported consumer.

## Source and generated contracts have different roles

Source discovery proves implemented operations within supported extractors. OpenAPI is a
generated contract baseline. The row-level inventory carries governance fields neither
source nor generated schema can infer safely.

## Dynamic provider surfaces need reviewed declarations

SDK-owned route families may not be statically enumerable. A version-pinned, reviewed
declaration can identify their bounded surface without weakening ordinary endpoint checks.

## Breaking change is a decision

The diff supplies evidence. Compatibility policy, consumer impact, versioning, rollout,
and exception authority determine the outcome.

## Takeaway

Treat API compatibility as governed product behavior. Combine deterministic diffs with
supported-baseline policy, consumer identity, lifecycle, ownership, and human decisions.

## Canonical CIS sources

- [API design and governance](../specs/api-design-and-governance-spec.md)
- [API governance profile](../references/api-governance-profile.md)
- [`cis api diff`](../manual/cis_api_diff.md)
- [`cis api validate`](../manual/cis_api_validate.md)

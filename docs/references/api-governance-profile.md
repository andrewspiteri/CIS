---
title: "Change Impact Studio API Governance Profile"
type: api-governance-profile
status: Draft
owner: "Andrew Spiteri"
last_reviewed: "2026-08-14"
review_cadence: "on API policy, host, or supported-version change"
cis:
  stable_id: change-impact-studio:reference:api-governance-profile
---

# Change Impact Studio API Governance Profile

CIS currently exposes a local CLI rather than an HTTP API. The profile is retained as
the canonical repository example and becomes active if an HTTP host is introduced.

## Settings

| Setting | Value | Status | Rationale |
|---|---|---|---|
| route-template | module/version/resource-path | Proposed | PARR-derived external route baseline, for example `/sales/v1/orders`. |
| compatibility-mode | forward-transitive | Proposed | Compare the current contract with every supported baseline in the same major version. |
| deprecation-window-days | 180 | Proposed | PARR baseline; another window requires a reviewed decision. |
| production-openapi-exposure | restricted | Proposed | Avoid unintentional operational metadata exposure. |
| correlation-header | repository-defined | Review Required | Select if an HTTP host is introduced. |
| client-header | repository-defined | Review Required | Select if an HTTP host is introduced. |

## OpenAPI documents

| Document role | Path | Host / deployment | Status | Evidence |
|---|---|---|---|---|
| current | none | none | Not Applicable | CIS has no HTTP host. |
| baseline | none | none | Not Applicable | CIS has no HTTP host. |

## Supported versions

| Module | Major version | Status | Sunset | Consumers | Evidence |
|---|---|---|---|---|---|
| none | none | Not Applicable | none | none | CIS has no HTTP API. |

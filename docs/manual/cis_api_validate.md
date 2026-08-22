---
title: "cis api validate"
type: manual
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-14"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-api-validate
---

# `cis api validate`

Enforces repository `API-*` rules across source, API rows, OpenAPI, permissions,
Problem Details, lifecycle, rate limits, versions, and public caching.

```text
cis api validate [--repo <path>] [--strict] [--no-refresh] [--format <human|json|agent>]
```

Validation refreshes discovery by default. `--no-refresh` uses existing local state and
fails with `CIS-API-STATE-001` when no normalized operation state has been built.
`--strict` promotes warnings to exit `5`; errors always produce exit `5`. Every diagnostic
contains a stable code, governing rule, evidence, and suggested remediation.

For `public` operations validation requires a governed cache, an indirect background or
precomputed population path, and no direct endpoint persistence evidence—including on
cache miss. Derived diagnostics live under `.cis/local/api/diagnostics.json`.

For `identity-protocol` operations validation instead requires an explicit `no-store`
policy, a named abuse-control/rate-limit policy, and no direct transport-to-persistence
evidence. Use this class only for credential-establishment, callback, and session protocol
traffic; ordinary unauthenticated application endpoints remain `public` and cache governed.

Consumers may name a local component, an approved generic external class, or a bounded
external identity such as `external:customer-web` when the consumer lives in another repository.
OpenAPI rows whose lifecycle is only `Draft` or `Planned` are recorded as intent and are not
loaded until they become implemented or active.

SDK-owned dynamic route families that cannot be enumerated as stable OpenAPI operations may
use the top-level `x-sdk-owned-surfaces` array. Every entry must provide a normalized
`pathPrefix`, a version-pinned `owner`, at least one `recipes` value, and existing
repository-relative `implementationEvidence`. CIS correlates the resulting
`ANY <pathPrefix>/*` dictionary row with that reviewed declaration and source evidence;
malformed or unverifiable entries fail with `CIS-API-OAS-004`.

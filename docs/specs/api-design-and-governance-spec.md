---
title: "API Design and Governance"
type: specification
status: Draft
version: "0.1"
scope: "Product:ChangeImpactStudio"
owner: "Andrew Spiteri"
last_reviewed: "2026-08-14"
review_cadence: "on API governance change"
cis:
  stable_id: change-impact-studio:spec:api-design-and-governance
---

# API Design and Governance

## Purpose and provenance

This specification imports and generalizes the proven API controller standard,
API dictionary, Problem Details catalogue, OpenAPI drift controls, and contract
maintenance workflow from PARR. PARR-specific hosts, identity providers, issuers,
and proprietary header names are intentionally not CIS defaults.

`MUST`, `MUST NOT`, `SHOULD`, and `MAY` are normative. Stable rule IDs are used
by generated tasks, repository policies, exceptions, tests, and review findings.

## Vocabulary

CIS uses these endpoint exposure classes:

| Class | Meaning |
|---|---|
| `public` | Deliberately unauthenticated endpoint |
| `identity-protocol` | Unauthenticated credential-establishment, callback, or session-protocol boundary that is explicitly `no-store` |
| `customer` | Authenticated customer-facing endpoint |
| `backoffice` | Authenticated administrative or operational endpoint |
| `internal/service` | Non-user service boundary |
| `webhook` | Signed inbound or outbound callback contract |
| `probe` | Minimal health or readiness endpoint |

PARR's term *Public API* maps to CIS `customer`; it does not map to CIS
`public`, which always means unauthenticated.

## Normative rules

### Exposure, scope, and authorization

- `API-EXP-01`: Every operation MUST declare exactly one exposure class in code,
  the API dictionary, and published OpenAPI.
- `API-SCOPE-01`: Customer or tenant scope MUST come from trusted server-side
  identity context. Callers MUST NOT choose trusted scope through routes, query,
  headers, or request bodies.
- `API-SCOPE-02`: Collections MUST be scope-filtered; individual resources MUST
  enforce object authorization; response properties MUST use explicit authorized DTOs.
- `API-AUTHN-01`: Authentication is required unless the explicit exposure class
  permits anonymous access. Basic authentication, query credentials, unsigned tokens,
  and non-expiring bearer tokens MUST NOT be introduced.
- `API-AUTHZ-01`: Protected operations MUST use explicit policy or capability
  authorization. Inline role checks do not replace the repository policy model.
- `API-PUB-01`: Every `public` endpoint MUST satisfy `PUBLIC-ENDPOINT-CACHE`:
  every response traverses a governed cache and no endpoint path accesses a database
  or repository directly, including on cache miss.
- `API-PUB-02`: Anonymous access MUST be explicit, approved, rate/size limited,
  monitored, and tested for abuse.
- `API-IDP-01`: `identity-protocol` operations MUST be `no-store`, use a named abuse-control
  policy, and keep persistence behind an application-service or SDK boundary. This class is
  not a general exemption from the Public Endpoint Caching Policy.

### Resources, routes, and identifiers

- `API-ROUTE-01`: Externally consumed routes SHOULD use
  `/{module}/v{major}/{resource-path}`. Approved probes, callbacks, and
  provider-constrained webhooks may use another recorded version contract.
- `API-ROUTE-02`: Prefer resource nouns and correct HTTP method semantics over RPC
  verbs. Keep nesting shallow unless the ownership relationship requires it.
- `API-ROUTE-03`: Module and major version MUST agree across routing, grouping,
  the API dictionary, supported-version registry, and OpenAPI.
- `API-SURF-01`: Assess a compatible extension, filter, projection, or representation
  before creating a near-duplicate operation. Never consolidate across distinct
  authorization boundaries, side effects, or resources.
- `API-ID-01`: Sensitive or customer-owned resources MUST NOT expose sequential
  internal primary keys. Opaque IDs do not replace authorization or rate limiting.

### Transport and boundary contracts

- `API-THIN-01`: Controllers and handlers bind and validate, obtain trusted scope,
  enforce policy, call one use case, map a response, and emit safe telemetry. They
  MUST NOT contain persistence, domain decisions, workflow orchestration, ad-hoc
  authorization, or raw exception handling.
- `API-DTO-01`: Request and response DTOs MUST be explicit. Domain and persistence
  models MUST NOT be directly bound, and writable fields MUST be allow-listed.
- `API-OAS-01`: Every externally reachable operation MUST appear in governed OpenAPI
  with purpose, schemas, statuses, authorization, pagination/filter/sort, scope source,
  idempotency, concurrency, rate limits, and error behavior.
- `API-DICT-01`: OpenAPI does not replace the row-level API dictionary. A material
  operation change MUST co-update implementation, API row, affected permissions and
  problems, OpenAPI, consumers, and tests.

### Errors, telemetry, and protocol security

- `API-ERR-01`: Expected failures MUST have stable 4xx semantics. Unexpected failures
  MUST use the repository Problem Details contract and MUST NOT reveal stack traces,
  SQL, topology, storage paths, secrets, or sensitive existence/state.
- `API-ERR-02`: Problem identities MUST be stable and catalogued. Correlation belongs
  in safe response metadata and server logs; internal diagnostics remain server-side.
- `API-LOG-01`: Never log credentials, authorization headers, connection strings,
  cryptographic material, raw evidence, or unjustified personal data. Request/response
  body logging requires an explicit, time-bounded, audited approval.
- `API-RATE-01`: Every endpoint MUST name a rate-limit policy. `429` behavior and
  `Retry-After` semantics MUST be documented where applicable.
- `API-HDR-01`: External transport MUST use TLS and appropriate security headers.
  CORS origins MUST be explicit; wildcard origins cannot accompany credentials.
  Principal-scoped responses SHOULD normally use `Cache-Control: no-store`.

### Reliability and data transfer

- `API-IDEM-01`: Retry-sensitive creates and commands MUST define idempotency or
  deduplication that prevents duplicate writes and side effects.
- `API-CONC-01`: Realistically contended updates MUST use ETag/`If-Match`, a version
  token, or an equivalent lost-update control.
- `API-EXEC-01`: Cancellation MUST flow downstream. Database, cache, broker, HTTP,
  and overall execution MUST use bounded timeouts. Long work SHOULD return `202`
  with a status locator and run outside the request.
- `API-COLL-01`: Collections MUST document pagination, supported filters/sorts, and
  a server-enforced maximum page size. Pagination MUST be stable; filters MUST NOT
  widen trusted scope.
- `API-FILE-01`: Uploads MUST constrain type and size and apply required scanning.
  Downloads authorize before storage resolution and never reveal internal paths or
  over-broad signed URLs.
- `API-PROBE-01`: Probes expose minimal status, not secrets, connection data,
  dependency versions, or internal diagnostics. Public probes require abuse controls.
- `API-WH-01`: Incoming webhooks verify signatures, resist replay, and are idempotent.
  Outgoing webhooks are scoped, timestamp-signed, bounded in retry, and SSRF-protected.

### Compatibility and lifecycle

- `API-VER-01`: Breaking changes require a new major version and explicit consumer
  migration. Supported majors remain independently routable, documented, and tested.
- `API-VER-02`: Each repository MUST record compatibility mode and deprecation window.
  The default is forward-transitive compatibility: the current contract MUST be
  compatible with every supported baseline in the same major-version line, not only
  the immediately preceding baseline. Public consumers retain at least a six-month
  window. Weaker compatibility or a shorter window requires a reviewed repository
  decision.
- `API-VER-03`: Deprecated operations MUST be marked in OpenAPI and SHOULD emit
  standard deprecation and sunset metadata. Retirement requires expiry of the approved
  window, migration guidance, and disposition of known consumers.

## Governed inventories

- The API dictionary owns operation inventory and endpoint permission usage.
- The permissions dictionary owns permission meaning and role/capability mappings.
- The Problem Details catalogue owns stable error identities and handling.
- The data dictionary owns persisted schema.
- OpenAPI owns the generated machine-readable HTTP contract baseline.

These artifacts are complementary. An API cannot become `Verified` while required
governance fields remain `unknown` or `TBD`.

The normalized operation model additionally records module, host/deployment, exposure,
consumer, request/response contracts, permission/authentication, trusted scope source,
OpenAPI operation/document, rate-limit policy, idempotency/concurrency, collection
semantics, Problem Details identities, cache policy, data-access path, lifecycle,
source locations, and known tests. This model is disposable state under `.cis/local/api/`;
the Markdown dictionaries and profile remain canonical.

`cis api discover` builds that state, `cis api inventory` routes over it, `cis api validate`
enforces cross-artifact rules, and `cis api diff` compares governed OpenAPI baselines.
Diagnostics carry a stable code, governing rule ID, evidence, and suggested remediation.

## Completion evidence

Completion requires deterministic OpenAPI export and baseline comparison, API and
related dictionary drift validation, authentication/authorization and abuse tests,
positive and negative contract tests, compatibility evidence, and explicit disposition
of known consumers. Production OpenAPI exposure MUST be an explicit repository decision
and SHOULD default to disabled or restricted.

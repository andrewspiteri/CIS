---
title: "Testing an Engineering Governance Tool"
type: article
status: Active
series: "Building Change Impact Studio"
series_order: 9
owner: "Andrew Spiteri"
last_reviewed: "2026-09-10"
review_cadence: on test strategy change
summary: "Verification must cover rules, safe mutations, lifecycle, idempotency, integration boundaries, and packaged behavior—not only individual methods."
cis:
  stable_id: change-impact-studio:article:testing-governance-tool
---

# Testing an engineering governance tool

CIS does more than transform input into output. It preserves authority boundaries,
rejects unsafe state, and coordinates lifecycle across modules. Tests must cover those
properties.

## Focused module tests

Each capability tests parsing, validation, identity, output, exit codes, idempotency,
and edge cases within its ownership boundary.

## Host and architecture tests

Composition tests verify explicit registration, duplicate protection, command dispatch,
and public contract boundaries.

## Cross-module delivery tests

Lifecycle tests exercise dossier creation, impact, decisions, planning, design,
execution preparation, verification, and authority gates together.

The current lifecycle also covers product-definition activation, BRD and technical-intent
author/review separation, provider execution, test/security reconciliation, CI evidence,
reference drift, and explicit result import.

## Filesystem safety tests

Temporary repositories verify path containment, collisions, dry runs, concurrent edits,
ownership, quarantine, and atomic failure behavior.

## Representative repositories

Golden paths expose integration gaps that fixtures miss: framework-generated files,
dynamic routes, multi-repository ownership, package tooling, and real build behavior.

Fake process and protocol providers keep cancellation, permission, timeout, resume,
malformed-result, redaction, and recovery tests deterministic. Mutation and coverage
evidence challenge safety-critical code that ordinary happy-path tests can leave weak.

## Package smoke tests

The installed .NET tool must register required modules and run outside the source build
layout. The editor package needs syntax, test, and packaging checks.

## Test invariants, not only examples

Important invariants include:

- Draft or proposed evidence never becomes Active authority automatically;
- stable IDs remain unique and are not silently replaced;
- a dry run performs no canonical mutation;
- invalid paths and collisions fail before writing;
- repeated confirmed operations are idempotent;
- stale baselines and digests block authority carry-forward;
- dependency repositories cannot receive product-owned writes;
- provider success does not imply evidence validity or completion; and
- deleting local derived state cannot remove durable meaning.

Property-oriented and parameterized tests can exercise these claims across many paths and
inputs instead of relying only on one happy fixture.

## Test failure halfway through

Governance failures often occur between valid steps: after files are staged but before
catalog replacement, after one repository is prepared but before a batch registry update,
or after a provider writes code but before it returns structured evidence. Fault injection
should prove rollback, retained diagnostics, and safe retry.

Concurrency tests change a planned file before apply and verify collision rather than
overwrite. Cancellation tests prove owned processes stop and attempt history remains
coherent.

## Verify output as a public API

Human, JSON, and agent formats need fixtures for success, empty state, warnings, bounded
omission, confirmation, collision, stale input, invalid evidence, and internal failure.
Standard output must remain parseable and diagnostics must remain on standard error.

Exit codes and schema fields are compatibility contracts for scripts, MCP, and the editor.

## Use mutation and coverage as questions

Coverage identifies code that did not execute; mutation testing challenges whether tests
would detect changed behavior. Neither is a quality verdict. Apply them especially to
path containment, authority transitions, result parsing, rollback, and security-sensitive
rules, then inspect surviving mutations and excluded code deliberately.

## Test packages and real repositories

Unit and integration suites operate in the source layout. Release smoke tests install the
tool and VSIX into isolated locations, enumerate modules, run representative commands, and
exercise initialized and uninitialized states. Golden paths use real frameworks and
multi-repository boundaries to reveal assumptions fixtures cannot model.

## Keep evidence reproducible

Record exact SDKs, package versions, fixtures, arguments, and platform limitations. A
flaky or unavailable suite remains visible evidence, not a pass recovered through retries
without explanation.

## Test documentation and implementation together

Manuals, schemas, templates, catalog entries, module inventories, and source behavior form
one contract. A new option without a manual, a changed lifecycle without migrated
templates, or a module absent from the packaged inventory is an incomplete product change.
Strict documentation validation belongs beside code tests rather than after them.

## Takeaway

Test the governance claims: stable authority, safe mutation, honest evidence, lifecycle
gates, and packaged behavior. A unit-green implementation can still violate the product's
control model.

## Canonical CIS sources

- [Delivery and assurance](../specs/delivery-and-assurance-spec.md)
- [Testing standard](../standards/testing-standard.md)
- [Implementation roadmap](../specs/implementation-roadmap.md)
- [Security testing and evidence](../specs/security-testing-and-evidence-spec.md)
- [Provider-neutral agent execution](../specs/features/agent-execution-coordination-feature.md)

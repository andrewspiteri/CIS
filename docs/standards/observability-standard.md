---
title: "Application Observability Standard"
type: standard
status: Active
targets:
  - backend
  - frontend
  - operations
owner: Repository maintainer
last_reviewed: 2026-08-15
review_cadence: on change
source_of_truth: This file
provenance:
  method: curated adaptation
  source_repository: PARR
  source_paths:
    - docs/generic/standards/backend-observability-logging-standard.md
    - docs/generic/standards/frontend-observability-standard.md
    - docs/generic/standards/observability-telemetry-operational-diagnostics-standard.md
cis:
  stable_id: change-impact-studio:standard:observability
---

# Application Observability Standard

## Purpose

Provide actionable, privacy-safe evidence for failures, performance, dependencies, and operational health.

This starter is a classification-safe adaptation of the listed PARR standards. Repository maintainers may strengthen it, record bounded exceptions, or supersede it through reviewed canonical changes.

## Scope

Applies to: backend, frontend, operations.

## Normative language

`MUST` and `MUST NOT` are mandatory. `SHOULD` is the expected default and requires recorded rationale when not followed. `MAY` identifies an allowed option.

## Rules

- **OBS-001** Material operations MUST emit structured telemetry with stable event identity, outcome, and correlation context.
- **OBS-002** Telemetry MUST NOT contain secrets, credentials, raw tokens, or unnecessary sensitive personal or business data.
- **OBS-003** Failures MUST preserve enough bounded context to diagnose the operation without exposing protected internals to callers.
- **OBS-004** External dependencies and asynchronous processing MUST expose latency, failure, retry, and saturation evidence where operationally material.
- **OBS-005** Operationally significant features MUST define health signals, alert ownership, and a recovery or support procedure.
- **OBS-006** Frontend telemetry MUST distinguish route, interaction, rendering, network, and handled-error failures without recording sensitive field contents.

## Verification

- `OBS-001`: Inspect emitted logs, traces, or events in representative tests.
- `OBS-002`: Run sensitive-data checks and inspect representative telemetry.
- `OBS-003`: Review error responses and internal diagnostic evidence.
- `OBS-004`: Review metrics, traces, alerts, and failure tests.
- `OBS-005`: Review dashboards, alerts, ownership, and runbooks.
- `OBS-006`: Review client telemetry schema and privacy tests.

Run `cis standards validate --strict` after changing this standard or its conformance mappings.

## Exceptions

An exception requires the rule ID, human approver, rationale, bounded scope, review or expiry condition, and compensating controls. CIS and agents cannot approve exceptions.

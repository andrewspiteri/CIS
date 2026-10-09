---
name: cis-observability-implementation
description: Implement actionable logs, metrics, traces, alerts, dashboards, and runbook updates. Use when changed behavior needs operational detection or diagnosis.
---

# CIS Observability Implementation

## Purpose

Ensure operators can detect, explain, and recover from the changed behavior.

## Workflow

1. Identify success, failure, latency, saturation, and business-health signals.
2. Use stable event names and low-cardinality dimensions.
3. Propagate correlation without logging secrets or sensitive payloads.
4. Define alert intent, thresholds, ownership, and runbook actions.
5. Verify telemetry locally or in a safe environment and preserve evidence.

## Completion evidence

Preserve affected scope, exact commands, outcomes, generated artifacts, skipped checks, and residual risk in the canonical task or verification record.

## Guardrails

Do not add noisy logs or metrics that have no operational consumer.

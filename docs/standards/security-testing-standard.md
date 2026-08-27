---
title: "Security Testing and Evidence Standard"
type: standard
status: Active
targets:
  - security
  - testing
  - verification
  - release
owner: Andrew Spiteri
last_reviewed: 2026-08-27
review_cadence: on scanner, exception, AI, CI, or release change
source_of_truth: This file
provenance:
  method: curated adaptation
  source_repository: PARR
  source_paths:
    - docs/generic/standards/security-sast-standard.md
    - docs/generic/standards/security-secret-scanning-standard.md
cis:
  stable_id: change-impact-studio:standard:security-testing
---

# Security Testing and Evidence Standard

## Purpose

Require portable deterministic security scanning, governed exceptions, redacted evidence, immutable tool provenance, and advisory local-only model summaries.

## Scope

This standard applies to security scanning, retained scanner evidence, release gates, exceptions, and AI-assisted triage across repository, backend, frontend, infrastructure, and release work.

## Normative language

`MUST` and `MUST NOT` are mandatory. `SHOULD` is the expected default and requires recorded rationale when not followed. `MAY` identifies an allowed option.

## Rules

- **SEC-TEST-001** Production source MUST run portable blocking SAST independently of optional platform security features.
- **SEC-TEST-002** Every repository MUST scan its complete revision for secrets and MUST redact secret material from reports, logs, prompts, and retained artifacts.
- **SEC-TEST-003** Dependency-bearing source MUST receive filesystem vulnerability scanning and Compose, Terraform, or equivalent deployment configuration MUST receive configuration scanning.
- **SEC-TEST-004** Every releaseable container image MUST be scanned by immutable image identity before promotion; rebuilding after a passing scan invalidates that evidence.
- **SEC-TEST-005** Externally reachable web or HTTP surfaces SHOULD receive DAST against a repository-managed representative runtime; authentication limits and unavailable credentials MUST remain explicit.
- **SEC-TEST-006** A scanner process success without a readable declared result artifact MUST be classified as invalid evidence, never passed.
- **SEC-TEST-007** Accepted findings MUST name the exact scanner and fingerprint, classification, bounded reason, owner, human approver, approval reference, and expiry.
- **SEC-TEST-008** Model-generated security summaries MUST use only normalized redacted findings, MUST stay local unless separately authorized, and MUST NOT change scanner severity, exceptions, or verdicts.
- **SEC-TEST-009** Security logs and results MUST be bounded, redacted, hashed, and correlated to scanner suite, workflow run, attempt, component, and repository revision.
- **SEC-TEST-010** Release verification MUST block on applicable unresolved critical or high findings, expired exceptions, invalid evidence, stale revision evidence, missing required security suites, or mutable scanner and external workflow-action references.

## Verification

- `SEC-TEST-001`: Reconcile a Semgrep or equivalent result artifact and inspect the deterministic verdict.
- `SEC-TEST-002`: Run governed secret scanning and inspect normalized evidence for absent secret values.
- `SEC-TEST-003`: Reconcile Trivy filesystem and configuration evidence for applicable components.
- `SEC-TEST-004`: Compare the scanned digest or image ID with the promoted artifact.
- `SEC-TEST-005`: Review ZAP or equivalent target, coverage, exclusions, and result evidence.
- `SEC-TEST-006`: Remove or corrupt a fixture result and run security reconciliation.
- `SEC-TEST-007`: Run `cis security exceptions validate --strict` and inspect acceptance governance.
- `SEC-TEST-008`: Inspect summary metadata, prompt digest, local-only route, and deterministic verdict preservation.
- `SEC-TEST-009`: Inspect the reconciled manifest and artifact metadata.
- `SEC-TEST-010`: Run release validation, compare the manifest revision and applicable suite states, and verify scanner digests and full action commit SHAs.

Run `cis standards validate --strict` after changing this standard or its conformance mappings.

## Exceptions

An exception requires the rule ID, human approver, rationale, bounded scope, review or expiry condition, and compensating controls. CIS and agents cannot approve exceptions.

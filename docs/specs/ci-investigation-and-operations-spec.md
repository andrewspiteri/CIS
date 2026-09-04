---
title: "CI Investigation and Safe Operations Specification"
type: specification
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on CI provider contract change"
cis:
  stable_id: change-impact-studio:spec:ci-investigation-operations
---

# CI Investigation and Safe Operations Specification

## Purpose

CIS investigates remote CI without treating a remote check as canonical delivery
authority. Provider-neutral normalized evidence supports diagnosis, local reproduction,
testing reconciliation, and release review.

## Provider and target model

`ICisCiProvider` owns availability, pull-request checks, workflow runs, jobs, job logs,
artifact metadata, and confirmation-gated failed-job reruns. Duplicate provider kinds
are conflicts. The initial GitHub provider uses `GH_TOKEN`, `GITHUB_TOKEN`, or the
authenticated GitHub CLI credential without printing it. Repository target resolution
uses explicit `--repository`, `GITHUB_REPOSITORY`, then the `origin` remote.

## Commands

Read-only commands are `providers`, `status`, `runs`, `jobs`, `logs`, `artifacts`,
`diagnose`, and `reproduce`. `rerun-failed` is a remote mutation and requires `--yes`.
No command approves a plan, risk, completion, release, or merge.

## Evidence and privacy

Downloaded job logs are capped at 10 MiB, redacted for known credential and authorization
patterns, content-hashed from the received bytes, and stored under `.cis/local/ci/`.
Diagnosis inspects at most five failed, timed-out, or cancelled jobs per run. Raw remote
artifact downloads are not automatic; artifact metadata remains sufficient for routing.

Failure classes are assertion/product, runner/infrastructure, missing prerequisite,
timeout, cancellation, and unknown. A diagnostic rerun never erases the first failure.
Suggested reproduction commands are advisory and must use repository-owned workflows
when available.

## Completion criteria

Provider registration, target inference, normalized payload parsing, path safety,
credential redaction, bounded evidence, failure classification, reproduction suggestions,
and mutation confirmation must have automated coverage. Human, JSON, and agent output
must represent the same result with stable exit codes.

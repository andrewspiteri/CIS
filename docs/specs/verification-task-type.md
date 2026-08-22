---
title: "Task Type: Verification"
type: task-type-definition
status: Draft
version: "0.1"
scope: "Product:ChangeImpactStudio"
owner: "Andrew Spiteri"
last_reviewed: "2026-08-13"
review_cadence: "on planning-model change"
cis:
  stable_id: change-impact-studio:task-type:core.verification
---

# Task Type: Verification

## Identity and boundary

| Property | Value |
| --- | --- |
| Stable type key | `core.verification` |
| Provider | CIS Plan core module |
| Creation policy | Always |
| Required predecessor(s) | Every applicable implementation, migration, operations, and rollout task; approved Visual Design for UI-bearing scope |
| Primary consumer(s) | Independent Assurance and Final Delivery Sweep |

Prove every requirement, acceptance criterion, prohibited behavior, compatibility promise, and regression boundary through reproducible proportionate evidence.

## Activation and inputs

### Creation evidence

- Every governed feature creates this task.

### Required inputs

- Requirements/exclusions, accepted impacts, decisions, task acceptance/negative criteria, implementation artifacts, baselines, risks, and applicable quality policies.

## Required activities

1. Build requirement-to-check coverage across unit, component, integration, contract, migration, UI journey, performance, security, and operational layers as applicable.
2. Run the narrowest checks during work and the proportionate affected/regression suite at completion.
3. Verify negative and denied behavior, not only happy paths.
4. Preserve exact commands, versions, environments, inputs/baselines, results, and artifact digests.
5. Record failures honestly and route unmet criteria to rework, deferral, or residual-risk decision.
6. For public endpoints, prove cache-path use and headers, hit/miss/refresh/stale and
   failure behavior, stampede protection, payload isolation, and the absence of a
   direct database/repository dependency from the endpoint boundary.

## Required outputs, dependencies, and authority

- Canonical `verification.md` evidence ledger.
- Requirement/negative-criteria and planned-check coverage matrix.
- Reproducible commands/results, failures, deferrals, and residual risk.

Only tasks without an unresolved dependency may run concurrently. Approved Visual
Design is a direct global dependency for every downstream UI-bearing task. Human
authority is required for scope changes, deferrals, residual-risk acceptance, and
any repository policy exception; task execution and evidence collection do not imply
that authority.

## Acceptance criteria

- [ ] Every acceptance and prohibited behavior has passing reproducible evidence or explicit approved residual risk.
- [ ] The affected regression surface is justified and passes without hidden skipped/filtered failures.
- [ ] Evidence can be independently rerun or inspected from the recorded baseline.
- [ ] `PUBLIC-ENDPOINT-CACHE`: deterministic architecture and integration evidence
      proves the unauthenticated endpoint is cached and persistence-isolated.

## Negative criteria

- Do not convert warnings, skipped tests, missing environments, or flaky retries into passes.
- Do not use an agent's narrative confidence as deterministic evidence.
- Do not close uncovered criteria through tracker status changes.

## Validation and completion evidence

- Validate evidence completeness, command reproducibility, exit codes, test filters, skipped/flaky results, artifacts, and requirement coverage.
- Record exact commands, tool versions, environment/baseline, counts, results, artifact paths/digests, and deferrals.

Completion evidence must identify the exact baseline, affected target, command or
review method, timestamp, actor/tool version, result, and artifact path or digest.
Open placeholders, unchecked acceptance/validation items, unexplained warnings, or
missing evidence prevent completion.

## Deferral and complexity

Any deferral records the unmet criterion, reason, owner, consequence, revisit
condition, residual risk, and human approval. Low for a bounded existing suite; medium for several layers/environments; high for performance, migration, distributed, safety/security, or broad regression scope and must be decomposed.

## External issue hints

- Title: `Verification: <feature title>`.
- Labels: `cis`, `task-type:core.verification`, plus affected repository/component labels.
- Body: canonical task link, source digest, targets, dependencies, gates, acceptance,
  validation, and evidence expectations.
- External status never grants CIS approval, accepts risk, or replaces the canonical
  Markdown task lifecycle.

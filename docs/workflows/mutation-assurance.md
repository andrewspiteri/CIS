---
title: "CIS mutation assurance workflow"
type: workflow-definition
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-28"
review_cadence: "on mutation scope, threshold, runner, or evidence change"
cis:
  stable_id: change-impact-studio:workflow:mutation-assurance
---

# Mutation assurance workflow

This release-tier workflow executes the bounded mutation profile separately from
ordinary pull-request verification. Its native Stryker JSON report remains derived
state under `.cis/local/testing/mutation/` and is reconciled through `cis test`.

| Step | Command | Working directory | Test suites | Depends on | Continue on failure | Timeout seconds |
|---|---|---|---|---|---|---:|
| cis-safety-mutation | pwsh tools/run-mutation.ps1 | . | cis-safety-mutation | - | no | 3600 |

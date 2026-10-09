---
title: "Projection Dictionary"
type: reference
status: Draft
owner: Repository maintainer
review_cadence: on change
cis:
  stable_id: change-impact-studio:reference:projection-dictionary
---

# Projection Dictionary

Governed by `change-impact-studio:spec:projection-dictionary`. CIS seeded 3 row(s) from deterministic repository evidence. Review them before changing this document to Active.

| Projection ID | Projection / read model | Source of truth | Producer / refresh | Consumers | Fields summary | Freshness / staleness | Sensitivity | Status | Evidence |
|---|---|---|---|---|---|---|---|---|---|
| PROJ-CIS-MODULES-REPOSITORY-TESTS-ORDER-SUMMARY-PROJECTION | OrderSummaryProjection | unknown | cis-modules-repository-tests | unknown | unknown | unknown | unknown | Draft | tests/Cis.Modules.Repository.Tests/RepositoryInitializerTests.cs |
| PROJ-CIS-MODULES-TESTING-CHANGED-COVERAGE-PROJECTION | ChangedCoverageProjection | unknown | cis-modules-testing | unknown | unknown | unknown | unknown | Draft | src/Cis.Modules.Testing/ChangedCoverageProjection.cs |
| PROJ-CIS-MODULES-TRACKER-TRACKER-TASK-PROJECTION | TrackerTaskProjection | unknown | cis-modules-tracker | unknown | unknown | unknown | unknown | Draft | src/Cis.Modules.Tracker/TrackerContracts.cs |

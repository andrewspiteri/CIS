---
title: "CIS assurance improvement candidates — 5 October 2026"
type: reference
status: Draft
owner: "Andrew Spiteri"
last_reviewed: "2026-10-05"
review_cadence: "before remediation planning"
cis:
  stable_id: change-impact-studio:reference:cis-assurance-improvement-candidates-2026-10-05
---

# CIS assurance improvement candidates

These product improvement candidates guide future CIS assurance work. They are not
claims about a particular application or an approved implementation plan. Each needs
an explicit CIS requirement, a reproducible public fixture, and verification before
it can be treated as resolved. Application-specific evidence is excluded.

| ID | Candidate improvement | Evidence needed in CIS |
| --- | --- | --- |
| SC-01 | Detect dense code that obscures behavior. | A maintainability rule and representative synthetic examples. |
| SC-02 | Assess responsibilities across large partial classes. | Dependency and ownership checks, beyond file counts. |
| SC-03 | Make code-quality expectations explicit. | A reviewed maintainability standard with clear applicability. |
| SC-04 | Support automated architecture boundaries. | Dependency-rule fixtures with known violations. |
| SC-05 | Recommend established test frameworks when no choice exists. | Bounded discovery and a recorded repository decision. |
| SC-06 | Keep test harnesses maintainable. | Reusable native-runner recipes and small fixture helpers. |
| SC-07 | Encourage independently diagnosable tests. | Examples with precise assertions and isolated failures. |
| SC-08 | Expose missing coverage and independent assurance. | Scope, reports, thresholds and reviewed exceptions. |
| SC-09 | Reconcile suite inventories with actual test commands. | Missing, stale and mixed-layer inventory fixtures. |
| SC-10 | Separate reusable test infrastructure from disposable output. | Versioned harness examples and artifact retention rules. |
| SC-11 | Make regression-gate scope visible. | Qualified local and CI recipes with reported omissions. |
| SC-12 | Reassess classification and standards as repositories grow. | Empty-to-implemented repository fixtures. |
| SC-13 | Connect delivery evidence to formal plans and test catalogues. | Traceability checks that preserve actual approval authority. |
| SC-14 | Distinguish passing configured checks from complete assurance. | Projections that expose unconfigured or unexecuted layers. |
| SC-15 | Include callers and operational effects in review context. | Review fixtures with cross-boundary consequences. |
| SC-16 | Check derived-context freshness and routing quality. | Stale graph/index and inaccurate-summary fixtures. |
| SC-17 | Keep representative workload acceptance separate from component success. | Bounded workload receipts and failure examples. |

Relevant CIS sources are the [testing standard](../standards/testing-standard.md),
[standard selector](../../src/Cis.Modules.Repository/DefaultStandardRegistry.cs),
[file-index specification](../specs/file-index-card-spec.md), and
[planning specification](../specs/change-impact-and-planning-spec.md).
The [testing harness defaults plan](../specs/testing-harness-defaults-plan.md) proposes
work on SC-05 through SC-12 and the testing aspects of SC-13 through SC-16.
The [code quality and architecture plan](../specs/code-quality-architecture-plan.md)
proposes work on SC-01 through SC-04 and related prevention for SC-12, SC-14 and SC-15.
Both plans remain Draft proposals; their publication does not close these candidates.
The [remediation record](../../planning/cis-trial-remediation-2026-10-05.md) documents
implemented CIS changes; those changes do not close every candidate in this inventory.

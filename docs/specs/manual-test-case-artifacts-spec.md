---
title: "Manual Test Case Artifacts"
type: specification
status: Draft
version: "0.1"
scope: "Product:ChangeImpactStudio"
owner: "Andrew Spiteri"
last_reviewed: "2026-08-25"
review_cadence: "on feature-planning change"
cis:
  stable_id: change-impact-studio:spec:manual-test-case-artifacts
---

# Manual Test Case Artifacts

## Purpose

Every imported feature must produce a reviewable manual-test catalogue and a
test-management import projection. Both formats come from one deterministic model so
they cannot silently disagree, and every case has durable traceability to executable
automated coverage before final delivery.

## Artifacts and authority

| Artifact | Role | Authority |
| --- | --- | --- |
| `changes/<id>/test-cases.md` | Human-readable catalogue, provenance, coverage, case definitions, and discovered automated-test references | Derived from the canonical feature specification and registered repository test sources |
| `changes/<id>/test-cases.csv` | Portable one-row-per-case import projection | Derived and hash-bound to `test-cases.md` |
| `changes/<id>/verification.md` | Manual and automated execution results | Human-reviewed canonical evidence |

Test definitions are regenerated through `cis plan import-spec` or `cis plan derive`.
Test execution results, failures, dates, environments, and tester identity never belong
in the generated catalogue; they remain verification evidence.

## Canonical case model

Every functional requirement produces one stable case with:

- `TC-<NORMALIZED-REQUIREMENT-ID>-001` identity;
- title and section;
- low, medium, or high priority inherited from requirement complexity;
- functional, UI, API/contract, security, accessibility, or data/persistence type;
- explicit environment, actor, data, configuration, and permission preconditions;
- numbered manual steps;
- the exact acceptance criterion as expected result;
- requirement reference;
- `public`, `customer`, `backoffice`, or `not-applicable` frontend type; and
- `Pending` or `Automated` automation status; and
- zero or more exact `<repository-id>::<repository-relative-path>:<line>` automated-test references.

IDs remain stable while the requirement ID remains stable. Changed requirement content
regenerates the case body and source digest without creating a competing identity.

## Markdown contract

`test-cases.md` records the change ID, source feature path and SHA-256, case count,
automated and pending counts, CSV
path and SHA-256, deterministic generation marker, and derived authority. It contains a
compact coverage table followed by complete case definitions with Preconditions,
Steps, and Expected result sections.

The Markdown catalogue is catalogued. It is the reviewable routing artifact and the
binding record for the CSV projection.

## CSV contract

The UTF-8 CSV uses CRLF records, RFC-style quote escaping, one row per case, and these
portable fields:

```text
ID,Title,Section,Priority,Type,Preconditions,Steps,Expected Result,References,Frontend Type,Automation Status,Automated Test References
```

Importers map these fields to TestRail or another system's project-specific fields.
Numbered steps use embedded line feeds inside the quoted Steps cell. Cells whose first
non-space character is `=`, `+`, `-`, or `@` are prefixed with an apostrophe to prevent
spreadsheet formula execution.

## Generation and validation

Feature import/derivation writes both artifacts atomically with the plan inputs.
Repeating unchanged input produces byte-identical files and an unchanged result.
Re-import after feature revision regenerates both projections and participates in the
ordinary plan-currency and renewed-approval workflow.

Planning permits `Pending` because implementation has not necessarily started. An
automated test reflects a case by containing its exact stable `TC-*` identity in the
test name, framework metadata, or an adjacent traceability annotation. CIS scans only
recognized test source paths across every registered workspace repository. It excludes
vendor, generated, coverage, build, and disposable `.cis/local/` paths. Re-import after
test implementation changes refreshes status and source references without invalidating
an otherwise unchanged approved plan.

Final `cis verify validate` requires every generated case to have a live recognized
automated-test reference and requires the catalogue to match the current source
locations. Missing coverage or stale mappings produce
`CIS-VERIFY-AUTOMATION-COVERAGE` and block acceptance. Manual execution remains
valuable evidence, but it does not replace this automated-coverage gate.

Plan validation fails when either artifact is absent, source path/digest is stale, the
case count differs from the requirement count, any requirement lacks coverage, the CSV
header is unsupported, or the actual CSV SHA-256 differs from the Markdown binding.

## Initial acceptance scenarios

- A one-requirement feature generates one stable case in both formats.
- A multi-surface feature preserves requirement and frontend classification in each row.
- Commas, quotes, and multiline steps import without changing field boundaries.
- Formula-like source content is neutralized in CSV.
- Tampering with the CSV fails plan validation; re-import restores the projection.
- Unchanged re-import performs no writes.
- A stable ID in a registered repository test source changes the case from `Pending`
  to `Automated` and records its exact repository/path/line location.
- A matching ID under `node_modules` or a production/documentation file is ignored.
- Final verification rejects pending, removed, or stale automated-test coverage.

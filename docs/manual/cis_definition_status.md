---
title: "cis definition status"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-08"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-definition-status
---

# `cis definition status`

```text
cis definition status [--workspace <path>] [--format <human|json|agent>]
```

Reports all eight pages, current/completion state, canonical artifact paths, issues, applicable
dictionaries, generated diagrams, UI-preview metadata, session identity, and consolidated
activation readiness. It is read-only and does not weaken the ordinary Active-document gates.

The page projections share repository classification, graph metadata, input hashes,
BRD discovery, and validation results within this calculation. Status reads graph
headers instead of loading all nodes and edges merely to obtain build identity.
Technical and UI questionnaires are included in the JSON result as `technicalQuestions`
and `uiQuestions`, so clients need not launch separate questionnaire status commands.
`businessInference` reports whether the BRD can be drafted and the product-owned repositories
available for existing-project inference, including their graph freshness. Dependencies and
a docs-only authority are excluded. These sources come from the canonical workspace registry;
reporting them neither invokes a provider nor changes the BRD.

The next calculation rechecks current inputs and file existence. Disposable graph
validation and database-hash caches avoid repeating unchanged work; see
[`cis graph validate`](cis_graph_validate.md) for invalidation and filesystem behavior.
Approval requirements and stale-document gates are unchanged.

BRD status and registered repository-source checks also use fresh graph metadata. Explicit
BRD discovery, validation and approval continue to perform deep graph validation. For local
phase timings on standard error, set `CIS_PERF_TRACE=1`; see
[`cis graph build`](cis_graph_build.md#local-performance-tracing).

`businessInference.lastPreparation` contains the last explicit existing-system inventory report
for owned repositories: dictionary paths, discovered/added row counts, actions, preservation
warnings and errors. Reading status does not repeat discovery or rebuild graphs. Rerun business
preparation after source changes; these counts are not a current semantic coverage assessment.

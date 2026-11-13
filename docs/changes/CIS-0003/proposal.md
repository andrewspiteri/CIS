---
title: "Shared outcome authority and proportionate planning"
type: change-proposal
record_format: proportionate
status: Proposed
change_id: CIS-0003
outcome: "An authorized outcome remains executable through task organization and ordinary refresh within one finite resource total."
baseline_kind: git
baseline: "464dcb8255f9a0c4a01652f290bae680e2e7afc0"
graph_build_id: "sha256:fe48e13b336fabc6256a5a0912631083c19ea01a073b744872fc63f031613cff"
impact_roots: "[]"
repository_baselines_path: creation-inputs.json
repository_baselines_sha256: "sha256:cddc1e155f4237d807abebe139ff1fed1328cd4f4dcb41c4c9ead557dda5c83b"
authority: derived-outcome-authorization
---

# CIS-0003: Shared outcome authority and proportionate planning

## Outcome

An authorized outcome remains executable through task organization and ordinary refresh within one finite resource total.

## Impact roots

No roots declared. Supply roots to `cis impact analyse`.

## Constraints

- Necessary tasks and supporting work derive from the recorded outcome authorization.
- Changes outside its scope or overall limits require a specific decision.

## Acceptance criteria

- Deliver the authorized outcome and its task-specific acceptance criteria within the shared constraints and limits.

<!-- cis:outcome-view -->

## Authorized outcome and limits

Derived from [the canonical authorization](authorization.json), decision `CIS-OUTCOME-WORKFLOW-CORRECTION-2026-11-13`, recorded for Andrew Spiteri. Tasks reference this shared context; their organization is not a separate human review.

One authorization carries planning and execution of the agreed CIS correction through task organization and ordinary refresh.

- `workflow`: Implement and verify the requested shared outcome authorization, bounded accounting, proportionate records, applicable gates, guidance and review; push only the development branch. Paths: `.github/skills`, `AGENTS.md`, `docs`, `src`, `tests`.

- No CISLIB consumer execution or alteration of live CISLIB state.
- No main merge or release; no historical cleanup delegation.
- Preserve legacy approvals, contracts, costs, attempts and valid results.
- Resource decision CIS-OUTCOME-WORKFLOW-CORRECTION-2026-11-13; approved input SHA-256 283c7c1f3ad296ba688fa8a4ed516e929291ca5597bd1bd8b08c5fac495ba130.

The total is 14,400 active seconds and 12 logical attempts, including 600 protected restoration seconds. Storage is 17,179,869,184 bytes including existing files and 268,435,456 protected reserve bytes.
Owned roots: `.artifacts/outcome-workflow-correction` (count only), `.cis/local`, `src` (count only), `tests` (count only).
Controllers: Codex. Task organization: delegated; resource distribution within the total: delegated.
Visual UI: not applicable; manual-test catalogue: no obligation recorded.
<!-- /cis:outcome-view -->

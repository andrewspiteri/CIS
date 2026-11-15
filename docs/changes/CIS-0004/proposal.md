---
title: "Preserve saved test reuse across ordinary continuation"
type: change-proposal
record_format: proportionate
status: Proposed
change_id: CIS-0004
outcome: "Unchanged relevant test inputs reuse their passing native result through closeout, commit, graph refresh and a new session with zero additional launches; genuine invalidation explains the specific material change, and compatible retained results survive a scoped verified CISLIB tooling update."
baseline_kind: git
baseline: "75283190777ad691811ade6a7eff247a74105728"
graph_build_id: "sha256:48a1ff107c851b978811099de7b26431e07e1c4689d0d56af5d34a05359387f0"
impact_roots: "[]"
repository_baselines_path: creation-inputs.json
repository_baselines_sha256: "sha256:c853b6b8de5b677151cf90d9061effa42699a1e21ebc7a51801e6db0b90729fb"
authority: derived-outcome-authorization
---

# CIS-0004: Preserve saved test reuse across ordinary continuation

## Outcome

Unchanged relevant test inputs reuse their passing native result through closeout, commit, graph refresh and a new session with zero additional launches; genuine invalidation explains the specific material change, and compatible retained results survive a scoped verified CISLIB tooling update.

## Impact roots

No roots declared. Supply roots to `cis impact analyse`.

## Constraints

- Necessary tasks and supporting work derive from the recorded outcome authorization.
- Changes outside its scope or overall limits require a specific decision.

## Acceptance criteria

- Deliver the authorized outcome and its task-specific acceptance criteria within the shared constraints and limits.

<!-- cis:outcome-view -->

## Authorized outcome and limits

Derived from [the canonical authorization](authorization.json), decision `CIS-INCREMENTAL-REUSE-2026-11-15`, recorded for Andrew Spiteri. Tasks reference this shared context; their organization is not a separate human review.

Reuse unchanged passing native results through ordinary closeout and new sessions, with precise material invalidation and safe CISLIB development-tool integration

- `reuse`: AC-1 normal CLI/native continuation zero-repeat; AC-2 selective explained invalidation; AC-3 captured compact material basis; AC-4 compatible retained results; AC-5 exact verified development build and read-only CISLIB profile dispositions, at most one fresh historical baseline under new ownership. Paths: `docs/changes/CIS-0004`, `docs/manual/cis_test_incremental.md`, `src`, `tests`.

- At most one fresh specifically noncurrent historical CISLIB profile; never reopen old owners.
- One shared 7200 seconds/3 attempts/180 included restoration across all tasks, agents and hosts; no full qualification campaign.
- Preserve existing CIS/CISLIB accepted work, reports and accounting; no main merge or release.

The total is 7,200 active seconds and 3 logical attempts, including 180 protected restoration seconds. Storage is 17,179,869,184 bytes including existing files and 268,435,456 protected reserve bytes.
Owned roots: `.artifacts/outcome-workflow-correction` (count only), `.cis/local`, `.cis/local/artifacts`, `src` (count only), `tests` (count only).
Controllers: Andrew Spiteri, Codex. Task organization: delegated; resource distribution within the total: delegated.
Visual UI: not applicable; manual-test catalogue: no obligation recorded.
<!-- /cis:outcome-view -->

---
title: "cis brd feature reconcile"
type: command-reference
status: Draft
owner: "Andrew Spiteri"
last_reviewed: "2026-10-08"
review_cadence: on command change
cis:
  stable_id: change-impact-studio:manual:cis-brd-feature-reconcile
---

# `cis brd feature reconcile`

Keep an unchanged, previously approved feature when a reviewed product-definition update remains compatible with its scope. Reconciliation updates the feature's definition binding and clears its approval to **Review Required**. The specification body remains unchanged. Use ordinary feature revision when requirements, routing or direction have changed incompatibly.

```text
cis brd feature reconcile --workspace <authority> --item HLT-FR-001
  --review .cis/local/compatibility/review.json
  --actor "Reviewer identity" --reason "Why the retained scope remains compatible"
  --format json
```

The review file and all referenced files must be authority-relative, bounded UTF-8 files inside the repository, without symbolic links, traversal or `.git` paths. The limit is 1 MiB per file, 32 MiB total and 128 comparison entries. Backlog and product-definition authority must be Active and current. The feature must retain its previously approved content and unchanged backlog item.

## Compatibility evidence

Use schema version 1. Compute `expectedFeatureSha256`, `comparison.sha256`, `currentSha256` and `prior.sha256` from raw file bytes, using `sha256:` followed by 64 hexadecimal digits.

The product-definition fields are native baseline identities, not individual file hashes. Read `previousProductDefinitionHash` from the feature's existing `cis.product_definition_hash`. Run `cis definition status --workspace <authority> --format json` and inspect page currency. Obtain `expectedProductDefinitionHash` from `baselineHash` in `<authority>/.cis/local/definition-wizard/session.json`. The wizard session's `active` flag means open or closed; it is not document approval status. Reconciliation separately checks that the consolidated authority is Active/current. Never calculate a replacement token or edit the activation record to force a match. The following is an abbreviated schema example; replace the descriptive hash placeholders with actual hashes and list every native product-definition artifact.

```json
{
  "schemaVersion": 1,
  "expectedFeatureSha256": "sha256:<reviewed-feature-file>",
  "previousProductDefinitionHash": "sha256:<feature-original-definition-binding>",
  "expectedProductDefinitionHash": "sha256:<current-active-definition-binding>",
  "comparison": {
    "path": ".cis/local/compatibility/comparison.md",
    "sha256": "sha256:<reviewed-comparison-file>"
  },
  "artifacts": [
    {
      "path": "docs/cis/specs/business-requirements.md",
      "currentSha256": "sha256:<current-artifact-file>",
      "prior": {
        "path": ".cis/local/compatibility/prior/business-requirements.md",
        "sha256": "sha256:<retained-prior-artifact-file>"
      },
      "assessment": "compatible",
      "reason": "Explain the source difference and why the feature remains valid."
    }
  ]
}
```

The native definition inventory covers business requirements, technical intent and questionnaires, architecture and components, UI direction, backlog and dictionaries. It respects selected document paths. An explicitly nonvisual product omits the UI preview documents. A provider without a supported inventory cannot reconcile a stale feature.

Every current artifact needs a distinct retained prior file, exact old/current hashes and a reason. `unchanged` requires identical bytes. `compatible` requires a source-backed explanation of the differences in the separately hashed comparison document. Missing, unresolved or incompatible evidence blocks reconciliation. Do not manufacture prior evidence by copying current files and labelling them historical.

CIS checks coverage, file integrity and current authority. It cannot establish the semantic truth of a compatibility assertion or prove that a supplied historical file belonged to the original activation merely from its hash. The reviewer must verify its provenance and compare requirements, technical decisions, architecture, contracts and exclusions. Reconciliation does not transfer the previous human approval.

## Result and recovery

Success returns `reconciled` with the current feature validation. Only the definition binding, approval metadata and catalogue status change. The catalogue is `<documentation_root>/catalog.yml`, with `documentation_root` configured in `.cis/repository.yml`. Original canonical files, an inventory, the review JSON and an audit record are retained under `.cis/local/feature-reconciliation/<id>/`. The record identifies the actor, reason and source hashes and records that no approval was granted.

If inputs drift or validation fails after a write, CIS attempts restoration of every affected file. An incomplete restoration reports the recovery inventory. Individual replacements are atomic; the group is not crash-atomic and does not provide multi-writer locking. Keep the recorded evidence and resolve any recovery error before continuing.

`blocked` returns exit 5. Invalid formats return exit 2 before mutation. An already-current, valid feature returns `unchanged` without changing approval, provided the reviewed feature and current definition tokens match.

Clearing approval can make an adopted feature source stale in the BRD and therefore the backlog. The returned validation retains that warning. Refresh and review those parent records through their normal workflow before seeking feature approval; reconciliation does not approve or rewrite them.

After reviewing the result, obtain authorized approval through `cis brd feature approve`, rebuild the graph and reconcile any adopted feature evidence into the BRD. Refresh dependent plans through supported commands and retain their earlier history. Old implementation runs, reviews and completion receipts do not become current merely because reconciliation succeeded.

## Continue after reconciliation

1. Run `cis graph build --workspace <authority>`, then [technical-intent refresh](cis_technical_intent_refresh.md). This safely reconciles BRD evidence and the downstream chain. If it reports a material difference, review that difference and use the corresponding authorized approval workflow; do not force an approval or edit hashes. [Backlog build](cis_brd_backlog_build.md) describes its separate reviewed-change gate.
2. When the parent authorities are current, validate the feature with `cis brd feature validate --workspace <authority> --item HLT-FR-001 --format json`. Obtain [feature approval](cis_brd_feature_approve.md) with explicit reviewer identity and rationale.
3. Rebuild the graph again. The now-approved feature is eligible source evidence, so run [BRD reconciliation](cis_brd_reconcile.md) or the safe refresh chain again to incorporate that renewed status. This is why evidence refresh can be required both before and after feature approval.
4. Preserve the existing plan and task history, then [reimport the feature](cis_plan_import_spec.md) or derive its plan using current authority. Review the generated scope and constraints. Fresh implementation verification, [independent agent review](cis_agent_run.md) and [completion context](cis_plan_task_completion_context.md) must establish current evidence before a [task completion transition](cis_plan_task_transition.md).

## Recover after incomplete restoration

Stop concurrent writers and keep the failure output and backup directory. Read its
`inventory.json`: each `path` is the original canonical destination and `backup` names
its saved original bytes in that directory. Preserve any intervening human edits in
separate files before recovery. Confirm each destination is still the intended local
canonical file, compare it with the saved original, and restore only the affected
originals. Keep the backup and review records. Rebuild the graph and validate the
feature and parent authorities before retrying; obtain a new reviewed file hash if
the source changed. A restoration attempt or retained approval field alone does not
establish current authority.

## Find the required artifact paths

For the built-in definition provider, the full default inventory below is relative to
`documentation_root` from `.cis/repository.yml`. Prepend that root to form each
review entry's authority-relative `path`.

- `specs/business-requirements.md`
- `specs/technical-intent-questionnaire.md`
- `specs/technical-intent-spec.md`
- `architecture/overall-solution-design.md`
- `architecture/high-level-architecture-diagrams.md`
- `references/component-sheet.md`
- `references/dictionary-index.md`
- `specs/ui-direction-questionnaire.md`
- `design/ui-direction.md`
- `design/ui-system-preview.md`
- `design/ui-system-preview.svg`
- `plans/high-level-backlog.md`
- `references/api-dictionary.md`
- `references/command-dictionary.md`
- `references/event-dictionary.md`
- `references/workflow-state-dictionary.md`
- `references/projection-dictionary.md`
- `references/permissions-dictionary.md`
- `references/configuration-dictionary.md`
- `references/data-dictionary.md`
- `references/problem-details-catalogue.md`
- `references/screen-route-map.md`
- `references/package-catalogue.md`
- `references/module-ownership-map.md`
- `references/business-invariant-catalogue.md`
- `references/traceability-matrix.md`
- `references/erd.md`

Apply any explicit selections in `.cis/product-documents.json`: roles `business`,
`technical`, `architecture`, `components`, `diagrams`, `experience` and `delivery`
replace the corresponding BRD, technical-intent, design, component-sheet, diagram,
UI-direction and backlog paths. Omit both UI preview files only when the selected
UI direction declares `cis.visual_ui: false`. A different loaded provider must supply
its own inventory; CIS rejects a provider without one.

If the session record is missing or its binding is inconsistent, return to the
[definition workflow](cis_definition_init.md), resolve its reported gaps, and obtain
any required human review and [activation](cis_definition_activate.md). Reconciliation
cannot create or repair product-definition authority. Keep the mismatching record as
evidence and never edit its hashes to force a match.

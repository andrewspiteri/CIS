# Guarded feature reconciliation after product-definition changes

Historical qualification record: candidate identities, commands and unresolved states below describe that checkpoint. For the completed Windows .NET milestone and current trial instructions, use the [readiness record](engineering-defaults-trial-readiness.md) and [trial guide](engineering-defaults-dotnet-trial-guide.md). Earlier receipts and findings remain retained.

Status: implemented, independently reviewed, qualified and exercised through isolated candidate 25. Native reconciliation and ordinary approval restored current Foundation authority while preserving the entire feature body. Fresh task reviews and completion remain pending.

## Original failure (resolved)

The five documentary amendments are applied. Their renewed approvals changed the consolidated product-definition digest. The Foundation feature still has its original approved bytes and points at the earlier digest. Native feature validation correctly reports it as Stale.

Before this implementation, the commands did not provide a bounded recovery path for this case. Feature approval requires a current product-definition binding. Starting the existing feature preserves its binding. Agent feature authoring accepts only Draft or Review Required content and requests substantive specification authoring. Rewriting an unchanged approved feature merely to refresh its provenance would create unnecessary scope and risk.

At that checkpoint, WORK-030 remained Blocked. Strict document validation and passing application tests cannot substitute for current feature authority.

## Implemented recovery contract

Add `cis brd feature reconcile` for deliberate review of an existing feature against a changed, currently approved product definition. Keep reconciliation separate from human approval, following the existing solution-design reconciliation pattern.

The command takes the feature item, actor, rationale, reviewed raw feature SHA-256 and expected current product-definition digest. It verifies the workspace, safe canonical paths, Active/current backlog and product definition, unchanged backlog-item identity and digest, complete feature structure, and exact reviewed source bytes. Reject missing authority, changed requirements or routing, unsafe paths, malformed input and concurrent drift without modifying canonical files. A rationale is a recorded assessment, not evidence that arbitrary source changes are harmless.

Compatibility also requires retained prior approved product-definition artifacts and a source-backed comparison against the current artifacts, covering business requirements, technical choices, architecture, contracts and exclusions relevant to this feature. Bind that comparison and its reviewed old/current artifacts to the reconciliation record. Matching SHA tokens establish freshness only. Reject missing prior evidence, unresolved differences or incompatible direction; those cases need feature revision and ordinary scope review. For this rehearsal, the retained approved amendment packet and original canonical snapshots supply the documentary delta; they do not permit arbitrary future baseline changes.

For a reviewed compatible feature, preserve its entire specification body, requirement IDs, targets, exclusions and human notes. The native service updates only its product-definition binding, resets its existing approval to Review Required, updates the catalogue and retains original files plus a reconciliation record. Use the repository's existing transactional file helpers and rollback conventions. Do not carry an old approval onto changed provenance automatically.

Validate the reconciled feature and obtain the applicable human approval through the ordinary native command. Rebuild the graph and reconcile adopted feature evidence into the BRD using existing services. If this causes another substantive difference, report it explicitly rather than automatically extending approval.

The feature's raw hash will change. Reimport the same feature into the existing fresh plan through supported commands, preserving a recoverable copy of the plan, task constraints and history. Review regenerated requirements, routing, dependencies and task scope before restoring bounded execution guidance. Never patch plan or task source hashes manually. Retain old runs as historical and start fresh implementation/review sessions and completion receipts.

## Verification and review

Use the existing xUnit suite and command-dispatch tests. Cover successful reconciliation with byte-preserved body, cleared approval, retained backup and audit record; unchanged-current idempotence; stale feature or product tokens; changed backlog scope; missing/inactive authority; invalid feature; unsafe paths; and transaction rollback. Verify that reconciliation alone cannot pass feature approval or complete a task.

Have a different review provider inspect the implementation and authorization boundaries. Package a new isolated candidate from tested bytes, retain its hash, and exercise the exact rehearsal failure and recovery through its native CLI. Preserve the current feature and failed validation as baseline evidence. Then renew current authority/security evidence and resume WORK-030 followed by the remaining fresh tasks.

Before resuming, explicitly verify the product-definition authority is Active/current, the feature is Active/current, the plan binds the refreshed feature, and all required source artifacts match. Do not infer authority from an activation command's exit code. The observed wizard `active: false` / `readyToActivate: true` output means the setup session is closed; inspection of `ProductDefinitionAuthority.Evaluate` and its retained evaluation confirms that the current product definition itself is Active. This resolves the status interpretation, not the feature's stale binding.

## Related generator fixes

During technical-intent renewal, native initialization regenerated customized component and integration sections from discovered project folders. The unapproved generated output was retained, and the previously reviewed sections were restored before approval. The preservation fix now retains reviewed project direction across repeated initialization after approval clears. Generated standards evidence still refreshes. Focused preservation tests pass; the broader suite found the standards-inventory regression, which is corrected and qualified by the final full suite. No new component identity is inferred.

Native BRD approval also exposed a previously hidden participant-baseline table. The existing native presentation formatter restores the evidence wrapper without changing its values or approval metadata. The approval generator now preserves that wrapper. Its focused regression passes, including repeated approval renewal; this changes presentation without granting approval authority.

This follow-up grants no final delivery acceptance, later workload decision, real-project import, release or active-installation replacement.

## Implementation qualification checkpoint

The native full BRD suite passed 193 cases before the final independent-review corrections.
A subsequent Release run passed 35 reconciliation/presentation cases. These counts overlap.
Five late malformed-authority command cases passed. The changed-backlog fixture initially
attempted an invalid direct edit; its corrected native BRD/backlog review case now passes.
Final Release execution passes 31 TechnicalIntent, 79 SolutionDesign, 33 Definition and 17 Host cases.
The first full TechnicalIntent run retained 30 passes and one standards-evidence failure;
that failure is preserved and superseded by the final 31-case passing run.

The independent review required four corrections: normalized path identities across
platforms, comparison/output separation, structured malformed-authority errors and
clear distinction between native baseline tokens and raw file hashes. Those changes
passed closure review. A further static review accepted the standards-evidence fix.
A separate unfamiliar-reader check prompted concrete continuation and recovery steps.
No Linux execution or multi-file crash atomicity is claimed.

The retained Foundation comparison now covers 25 artifacts. A native baseline computation
reproduces the original feature binding exactly from historical copies, with no missing
artifacts. Fifteen current/prior files are byte-identical; ten have individually explained
compatible changes, including the earlier authorized standards refresh and source adoption.
Independent source review verified every copied prior file against its retained source and
accepted the comparison. This neither renews feature approval nor completes WORK-030.

## Current checkpoint - 8 October, candidate 25

The capability is implemented, qualified and exercised successfully through the isolated candidate. Fresh WORK-030 is now Complete. Follow the [current final-sweep checkpoint](engineering-defaults-work180-final-sweep.md#current-checkpoint---8-october-candidate-27) for subsequent task qualification and readiness; the original failure discussion above is historical.

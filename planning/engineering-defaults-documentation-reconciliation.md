# Engineering-defaults documentation reconciliation — 9 October 2026

The isolated worktree documentation now routes readers to the [current Windows .NET trial](engineering-defaults-dotnet-trial-guide.md) and its [candidate-34 qualification](engineering-defaults-trial-readiness.md). Earlier plans and candidate checkpoints retain their original decisions and evidence, with explicit links to current status.

## Changes reconciled

- Updated root/documentation indexes, engineering design-plan dispositions, assurance coverage and provider guidance. Draft document metadata and existing approvals remain unchanged.
- Corrected bootstrap, execution, resume/recovery and completion instructions. Command rules are per attempt; native provider permissions do not establish OS or working-directory confinement. Bundled CLI tests and the separate two-provider synthetic interval fixture have distinct evidence.
- Updated checked-in skills/instructions and the starter templates that generate bootstrap and agent guidance. Reinitialization with the updated source build preserves customizations and no longer restores the outdated command syntax or task path.
- Added 70 source-discovered configuration, module and package entries, then reviewed their descriptions and lifecycle. New ownership/dependency entries remain Draft. Configuration distinguishes environment variables from Stryker JSON, and marks the example database connection as sensitive. Added the synthetic reading command to the command dictionary.
- Distinguished rebuildable caches from native results, transcripts and receipts that must be retained while they support verification.

## Verification and package boundary

The existing native repository-initializer suite passed **126 tests**, with zero failed or skipped. A fresh local authority plus declared-C# initialization generated the corrected guidance, and strict skill validation passed. Root initialization applied only the reviewed starter-manifest update; its repeat preview reported no creates, updates, collisions or errors.

Strict documentation, standards and skill validation passed, and deterministic skill audit was clean. Source-aware and separate unfamiliar-reader reviews checked the user guidance; both were model-assisted, not human approval or real-user usability testing. Local command results, review dispositions, changed-file hashes and closing graph/Doctor receipts are retained under `.cis/local/qualification/documentation-reconciliation/`.

Candidate **0.3.0-engineering.20261009.34** and its existing package/source receipts remain unchanged. The starter wording corrections were tested from the updated source build; they have not been repackaged into candidate 34. A future build must carry its own package qualification. No active installation, branch merge, release or product acceptance changed.

## Reference-discovery limitations

The unrestricted `references reconcile` preview failed when discovery selected a screen-route map whose canonical file is absent. Explicitly selecting the existing configuration, module and package families succeeded. The command manual now documents that boundary. Fixing the command's missing-table handling remains separate implementation work.

Proposed rows also required semantic corrections: JSON configuration was rendered as environment variables, and a connection string was initially marked nonsensitive. The canonical documentation is corrected; automatic rendering still needs a separate implementation fix. Discovery candidates in data, permissions and workflow families remain warnings for review. They do not establish approved product entities, permissions or workflows, and this reconciliation does not claim a clean strict reference-governance audit.

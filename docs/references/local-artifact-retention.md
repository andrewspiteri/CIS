---
title: "Change Impact Studio Local Artifact Retention"
type: local-artifact-retention
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-11-09"
review_cadence: "on evidence, storage, privacy, or recovery-policy change"
cis:
  stable_id: change-impact-studio:reference:local-artifact-retention
---

# Change Impact Studio local artifact retention

Rules apply only to immediate entries beneath the declared `.cis/local/` path. Canonical Markdown and user-managed repository files are never eligible.

| Family | Path | Retain days | Keep latest | Action | Maximum bytes |
|---|---|---:|---:|---|---:|
| graph | .cis/local/graph | 0 | 0 | preserve | |
| api | .cis/local/api | 0 | 0 | preserve | |
| references | .cis/local/references | 0 | 0 | preserve | |
| frontend | .cis/local/frontend | 0 | 0 | preserve | |
| policy-impact | .cis/local/impact | 0 | 0 | preserve | |
| index-cards | .cis/local/index-cards | 0 | 0 | preserve | |
| brd-question-suggestions | .cis/local/brd/questions | 0 | 0 | preserve | |
| context | .cis/local/context | 30 | 20 | archive | |
| workflows | .cis/local/workflows | 30 | 20 | archive | |
| testing | .cis/local/testing/runs | 30 | 10 | archive | |
| mutation-runs | .cis/local/testing/mutation/cis-safety/runs | 1 | 2 | delete | 536870912 |
| security | .cis/local/security/runs | 30 | 10 | archive | |
| ci | .cis/local/ci | 14 | 20 | archive | |
| diagnostics | .cis/local/diagnostics | 30 | 20 | archive | |
| agents | .cis/local/agents | 30 | 20 | archive | |
| generation | .cis/local/generate | 30 | 20 | archive | |
| ai-cache | .cis/local/ai/cache | 7 | 50 | delete | |
| ai-qualification | .cis/local/ai/qualification | 90 | 20 | archive | |

`Maximum bytes` is optional. A missing or empty cell means no byte budget; existing five-column profiles remain valid. Values are integer bytes, so 536870912 means 512MiB. Newest entries protected by `Keep latest` remain protected under byte pressure. Outside that allowance, age makes an entry eligible; if projected live usage is still over budget, the oldest remaining unprotected entries also become eligible before reaching that age. `preserve` never removes entries.

Review `cis artifacts plan --family mutation-runs --format agent` after run writers finish. It reports the bytes eligible for removal and the projected live size. If the newest two runs alone exceed 512MiB, planning reports the unmet budget and blocks cleanup; review the protected allowance or budget explicitly. The profile does not cap writes or schedule deletion. Cleanup requires a separate `cis artifacts clean --family mutation-runs --yes` after plan review. A deletion leaves hashes, not recoverable content.

Diagnostic and iteration artifacts are transient. Keep a native failing report and
only the context needed to investigate an open issue. Delete successful canary
captures, duplicate reports and superseded attempts when they are no longer useful;
delete the remaining diagnostic material when the issue is fixed. Keep a brief
outcome and commit reference where useful. A current qualification or release
record follows its explicit retention requirement; ordinary iterations do not
become permanent release evidence.

These lifecycle decisions require a reviewed cleanup after writers finish. The
planner uses age, count and bytes; it does not infer whether an issue is resolved.
Review the profile before removing a protected entry, and preserve evidence still
needed by an open issue or current gate. Do not create archive bundles merely to
retain completed diagnostics. Full source/runtime snapshots and verbose logs are
reserved for bounded investigations.

Archive families remain reversible, but their ZIPs, manifests and retrieval copies consume disk. Output reports configured live bytes and artifact-management bytes separately; this local total excludes unconfigured paths and scratch trees elsewhere. The family byte budget applies only to configured live data.

Governed native work also has an owner-wide ceiling in the existing `cis agent work allocate` input. Its optional `storage` object supplies the approved `maximumBytes`, `roots` (each with `repositoryPath` and a relative `path`), `restorationBytes` inside that ceiling, and any `cleanupControllers` delegated by the allocation authority. A ceiling reference alone is not a numeric allocation. Use the actual authorized values once at the owning boundary; test profiles and retention family budgets cannot grant them. Declaring `.cis/local` covers the authority's known local outputs; associated repositories must be exact registered owned repositories. The known agents and artifact-management directories are conservatively included across those repositories, including other task data; this counts bytes without granting deletion authority. An explicit root with `scratch: true` may count native build/scratch output elsewhere inside a registered owned repository. Such count-only roots remain protected; source, user-managed data and `.git` never become retention cleanup candidates. Moving data into ZIPs or retrieval copies cannot escape the ceiling.

`cis artifacts inventory` and `plan` accept `--change`, `--task`, `--parent-execution`, `--authority` and `--actor`. They remain read-only and show missing or unreadable ownership as unknown, with a next action. Mutating operations inherit a governed parent or use that explicit association. They reserve the same owner's finite time and storage before creating new output. Existing `--yes` confirmation remains necessary for compact, clean and restore; it is not a new human approval when an authorized controller already has cleanup delegation.

Owner output distinguishes actual regular-file bytes, protected bytes, full pending growth reservations, remaining capacity and the restoration reserve. Overlapping paths count once; physically separate copies count separately even when hashes match. ZIPs are already included in management bytes and are not added twice. Exact CIS-owned native scratch is included while registered, including trusted guard extraction. Pending reservations conservatively retain their full allowance until safely settled, even after some bytes are written. A separate bounded allowance for atomic accounting writes is reserved from the same ceiling. Measurement covers declared paths and bounded monitoring, not arbitrary machine files, filesystem allocation metadata or instantaneous writes.

The shared ceiling and each operation's growth grant are checked separately. A
reservation retains its original baseline and the writable paths whose growth it
covers. Native work includes the target repository's declared output and build
roots plus its exact scratch; retention includes its management output and any
claimed restore destinations. Another admitted operation's writes in disjoint
owned roots still count toward the shared ceiling, but do not spend this
operation's grant. Operations sharing a writable native root retain the existing
exclusion; disjoint owned targets can proceed independently. Authority and
provider accounting directories remain globally counted and protected, with their
reserved write headroom, rather than being attributed to a native test's output
growth. A sampled retention check refreshes the same reservation without granting
another allowance. None of these measurements grants supersession or deletion
authority.

Active, uncertain and current required native evidence and durable accounting remain protected. Successful authorized deletion reconciles surviving bytes but never resets time or attempts. Protected evidence that prevents fitting the ceiling holds expansion: inspect the reported usage and plan, perform only delegated eligible cleanup, or return to the original allocation authority. The existing management area is not a configurable delete family; this checkpoint does not add an archive-pruning permission. Ordinary archive retrieval and restoration still need their finite operation allowance; they do not acquire the special time reserve for genuine native baseline recovery.

Archive restoration rechecks the original destinations against the current owned roots and protected evidence before claiming or writing those paths. An old archive does not grant path ownership. Testing destinations also use the existing native output lease, including when the archive's original family differs from the current profile.

Before substantial supported verification output, associate its declared locations and cleanup point with the existing task owner and reserve growth through the existing native/artifact route. Reuse compatible prepared inputs and normal caches instead of another unchanged campaign copy. Keep isolation where mutation or integration actually requires it; after restoration, collection or verified transfer, retire disposable owned workspaces, transfer bundles and staging copies when no current operation needs them. Retain required native reports, exact input identity and bounded failure diagnostics or a necessary reproducer. Exclude generated evidence from subsequent source/input enumeration unless a fixture or release input has a narrow explicit reason. Inventory and existing delegated cleanup decide eligibility; this guidance grants no new deletion authority and no control over arbitrary external shell or remote output.

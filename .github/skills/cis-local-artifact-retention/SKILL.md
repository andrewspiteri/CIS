---
name: cis-local-artifact-retention
description: Inventory, preview, compact, retrieve, restore, and clean derived CIS local artifacts. Use when `.cis/local/` grows, before archiving a completed delivery, or when older diagnostic/test evidence must be recovered.
---

# CIS local artifact retention

1. Read `docs/references/local-artifact-retention.md`.
2. Run `cis artifacts inventory --format agent` and `cis artifacts plan --format agent`.
3. Compact `archive` families only after review; CIS verifies ZIP contents before removing originals.
4. Retrieve archives non-destructively for inspection.
5. Restore with `--yes`; conflicts are never overwritten.
6. Clean `delete` families with `--yes`; a hash tombstone remains.
7. For governed work, inherit the admitted parent or supply `--change`, `--task`, `--authority`, `--parent-execution` and `--actor`. Resolve missing finite `storage.maximumBytes` and explicit roots once through the existing allocation authority; retention budgets and native profiles cannot allocate capacity. Inspect actual/protected/reserved/remaining owner bytes before expansion. Pending full growth and accounting-write headroom remain conservatively reserved until safe settlement. A cleanup actor needs existing delegation; no routine new human acknowledgment is required.

Only configured immediate entries beneath `.cis/local/` are eligible. Canonical and user-managed paths are outside this authority.

Count live output, archives/manifests, retrieval copies and exact owned scratch without overlapping paths twice. Keep exact controls, current required evidence, active/uncertain output and accounting protected. Successful deletion frees measured storage, never time or attempts. A storage or ownership hold is not permission to delete unknown material. The artifact-management root and unknown descendants are not retention families. Explicit delete or preserve policies may cover its known generated `archive`, `retrieved`, `restore` and `cleanup` subtrees; never re-archive them recursively. Existing leases and required-result protection still apply. Ordinary archive restore does not spend native baseline-recovery time reserve.

Check required/active ownership and supersession authority under existing leases before pruning or replacing prior report bytes; retain the bytes or return a targeted hold. Check the reservation's original operation baseline/domain/grant separately from total owned bytes. Another admitted operation's disjoint growth counts globally without spending this grant; shared writable native roots remain exclusive. Refreshing the same reservation grants no new allowance.

Plan substantial supported output locations and their cleanup point under the existing owner before writing. Reuse compatible prepared inputs/caches. After required restoration/result collection or a verified transfer, retire disposable owned workspaces, transfer bundles and staging copies through existing authorized cleanup when no operation needs them. Keep required reports/input identity and bounded diagnostics, not every campaign. Exclude generated evidence from later input enumeration unless explicitly required. External shell/remote output outside the supported route is not controlled by the storage ceiling; report that limit instead of claiming a machine-wide sandbox.

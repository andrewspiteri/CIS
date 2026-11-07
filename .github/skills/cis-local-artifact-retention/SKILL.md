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

Count live output, archives/manifests, retrieval copies and exact owned scratch without overlapping paths twice. Keep current required evidence, active/uncertain output and accounting protected. Successful deletion frees measured storage, never time or attempts. A storage or ownership hold is not permission to delete unknown material. Archive management is not a delete family; ordinary archive restore does not spend native baseline-recovery time reserve.

Check required/active ownership and supersession authority under existing leases before pruning or replacing prior report bytes; retain the bytes or return a targeted hold. Check the reservation's original operation baseline/domain/grant separately from total owned bytes. Another admitted operation's disjoint growth counts globally without spending this grant; shared writable native roots remain exclusive. Refreshing the same reservation grants no new allowance.

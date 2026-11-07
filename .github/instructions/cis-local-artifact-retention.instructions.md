---
applyTo: "**"
---

# CIS local artifact retention authority

- Canonical policy lives in `docs/references/local-artifact-retention.md`; all artifact-management state is derived.
- Preview before mutation. Compact, clean, and restore require `--yes`.
- Verify archive content and digest before removing originals.
- Retrieval is non-destructive; restore refuses conflicting content.
- Delete-policy tombstones preserve hashes but cannot recover bytes.
- Never target canonical, source, configuration, user-managed, or non-`.cis/local/` paths.
- Governed operations inherit or explicitly bind the existing change/task/parent and reserve finite time and owner storage before writes. Resolve numeric storage policy at the original allocation boundary; family budgets and test profiles grant no new capacity.
- Count live, archive/manifest, retrieval and exact owned scratch bytes once per physical path. Keep actual bytes separate from pending full-growth and accounting-write reservations; preserve current required, active/uncertain and accounting evidence.
- Check required/active ownership and supersession authority under existing leases before pruning or replacing report bytes; retain them or return a targeted hold. Check the original operation baseline/domain/grant separately from the shared ceiling; disjoint foreign growth counts globally, and shared writable native roots remain exclusive. Refresh the same reservation without another allowance.
- Use only existing cleanup delegation and confirmation. Reconcile surviving bytes after deletion without resetting time or attempts. Unknown ownership or a protected-storage hold never authorizes deletion; ordinary archive restore is not native baseline-recovery authority.

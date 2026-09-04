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

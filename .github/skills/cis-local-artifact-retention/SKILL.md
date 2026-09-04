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

Only configured immediate entries beneath `.cis/local/` are eligible. Canonical and user-managed paths are outside this authority.

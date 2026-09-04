---
title: "Local Artifact Retention and Recovery"
type: technical-specification
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on artifact family, storage, privacy, cleanup, or recovery change"
cis:
  stable_id: change-impact-studio:spec:local-artifact-retention
---

# Local artifact retention and recovery

## Scope

CIS applies retention only to configured immediate entries below `.cis/local/`. Paths containing traversal, paths outside that root, and the artifact-management tree itself are invalid. Canonical Markdown, source, configuration, and user-managed files are never candidates.

Each family defines a repository-relative path, age in days, number of newest entries to retain, and one action:

- `preserve`: report size and hashes, never remove;
- `archive`: compact eligible entries into a verified reversible ZIP;
- `delete`: remove eligible derived entries after writing a hash tombstone.

Inventory and plan are read-only. Compact, clean, and restore require explicit `--yes` confirmation.

## Archive integrity

Compaction hashes every immediate file or directory entry, creates a ZIP using repository-relative names, extracts it to isolated temporary storage, and verifies every entry digest before moving the ZIP into place. Only then are originals removed. A manifest records archive identity, family, timestamp, ZIP path/digest, entry kind, size, digest, and last-write time.

Retrieval verifies the ZIP digest and extracts all or one retained entry into `.cis/local/artifacts/retrieved/` without changing original locations. Restore additionally preflights original locations and refuses different existing content; it never overwrites a conflict.

Delete cleanup creates a tombstone with the same entry metadata before removal. A tombstone proves what was removed but cannot recover its bytes.

## Idempotence and failure

No candidates produces `unchanged`. Invalid policy, missing archives, digest mismatch, traversal, and restore conflicts fail before destructive work. Archive and cleanup records are excluded from their own retention families to avoid recursive deletion authority.

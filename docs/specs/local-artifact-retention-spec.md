---
title: "Local Artifact Retention and Recovery"
type: technical-specification
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-10-26"
review_cadence: "on artifact family, storage, privacy, cleanup, or recovery change"
cis:
  stable_id: change-impact-studio:spec:local-artifact-retention
---

# Local artifact retention and recovery

## Scope

CIS applies retention only to configured immediate entries below `.cis/local/`. Paths containing traversal, paths outside that root, and the artifact-management tree itself are invalid. Canonical Markdown, source, configuration, and user-managed files are never candidates.

Each family defines a repository-relative path, age in days, number of newest entries to retain, and one action. An optional sixth `Maximum bytes` column sets a non-negative integer byte budget for the family's live entries. Existing five-column profiles and empty budget cells retain age-only behavior. Family paths must not overlap or include the artifact-management tree.

- `preserve`: report size and hashes, never remove;
- `archive`: compact eligible entries into a verified reversible ZIP;
- `delete`: remove eligible derived entries after writing a hash tombstone.

Inventory and plan are read-only. Compact, clean, and restore require explicit `--yes` confirmation.

## Byte-pressure planning

Entries are ordered newest first using the latest contained file timestamp, with path as a stable tie-breaker. `Keep latest` always protects that many entries. Outside that allowance, entries older than the age limit are eligible as before. If the remaining live bytes exceed `Maximum bytes`, the oldest additional unprotected entries become eligible even if they are younger than the age limit. A `preserve` family never gains removal candidates.

Plans report live, protected, candidate, and projected live bytes for each family, with separate `budgetExceeded` and `budgetBlocked` flags. If protected entries alone exceed the budget, planning reports an error and destructive actions remain blocked until the policy is reviewed. It never deletes protected entries to make the numbers fit. A budget is a planning trigger, not a filesystem quota or an automatic cleanup schedule. Concurrent writers can increase usage after planning; review candidates after their writers finish.

Results separately report configured live bytes, archive ZIP bytes, and the whole artifact-management tree, including manifests, tombstones and retrieval copies. `totalReportedBytes` is configured live bytes plus management bytes; archive bytes are already included in management and must not be added twice. Unconfigured local paths and scratch directories elsewhere are outside that total. Archiving can increase the overall footprint and does not establish a bounded disk policy.

## Archive integrity

Compaction hashes every immediate file or directory entry, creates a ZIP using repository-relative names, then streams its expanded content through bounded hashing before moving the ZIP into place. Verification does not create a full extracted temporary copy. It checks every candidate's expanded size and digest, retaining the existing directory hash convention of sorted relative file names followed by their bytes. Only then are originals removed. A manifest records archive identity, family, timestamp, ZIP path/digest, entry kind, size, digest, and last-write time.

Archives retain the 100,000-entry and 20GiB expanded-size limits. Verification rejects traversal, duplicate paths, symbolic links, undeclared entries, missing content and digest or size mismatches. Actual streamed bytes must match each ZIP entry's declared length; directory entries have no payload. Retrieval and restore verify the archive's content against the manifest before extracting requested content, including when the ZIP digest itself has been rewritten.

Retrieval verifies the ZIP digest and extracts all or one retained entry into `.cis/local/artifacts/retrieved/` without changing original locations. Restore additionally preflights original locations and refuses different existing content; it never overwrites a conflict.

Delete cleanup creates a tombstone with the same entry metadata before removal. A tombstone proves what was removed but cannot recover its bytes.

Mutation run directories use a small delete-policy budget by default. Keep the native report and compact assessment in the mutation evidence root outside transient `runs` before cleaning bulky execution diagnostics. Copying all diagnostics into archives forever is not the default retention strategy.

## Idempotence and failure

No candidates produces `unchanged`. Invalid policy, missing archives, digest mismatch, traversal, and restore conflicts fail before destructive work. Archive and cleanup records are excluded from their own retention families to avoid recursive deletion authority.

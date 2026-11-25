---
title: "CIS-0005 exact stale Trivy cache cleanup"
type: implementation-plan
status: Approved
change_id: CIS-0005
---

# Recover scanner headroom within the existing ceiling

The required pinned Semgrep image cannot yet be admitted within the source's
remaining ordinary storage. Its layer metadata alone describes approximately
1.67 GB of uncompressed tar data, before uncertain shared-layer reuse and Docker
storage overhead. No image was pulled. Retain the existing 16 GiB ceiling and
256 MiB restoration reserve.

## Exact proposed removal

Remove only these two stale, mutable download-cache files, after rechecking their
paths, ordinary-file status, hashes and current drainage:

| Repository-relative path | Bytes | SHA-256 |
| --- | ---: | --- |
| `.cis/local/security/cache/trivy/db/trivy.db` | 1,325,740,032 | `662919df912102031ebad5fe23e15d26a4fb4d77c7c16f5c24900888001c3deb` |
| `.cis/local/security/cache/trivy/db/metadata.json` | 153 | `ade93b828e7c19201d75fa6175e9eb486ab8ec8ab03e59c0b27bca0176e55290` |

The expected logical-size recovery is **1,325,740,185 bytes**. Execution uses exact
literal file paths, with no recursive directory deletion. Retain a hash/size and
decision manifest and the observed post-removal count.

## Ownership and preservation

These files are inside the declared `.cis/local` storage root, are Git-ignored,
and are produced by `tools/run-security-scan.mjs`. Their database metadata records
an August 28 download with an August 29 next-update date. Only Trivy filesystem
and configuration modes mount this cache; Semgrep and Gitleaks do not.

The bounded reference inspection found no exact cache/database reference in
current work control or corrections, security run manifests, workflow states or
the current source artifact records. Independent process/container inspection
observed no scanner processes, running containers or source mounts, and no current
cache claim. Repeat the relevant checks immediately before removal.

Preserve the policy cache, every scanner result and run record, all raw native
reports, binaries, source graph/recovery data, archives and accounting files.
Debug outputs were inspected but are not included in this proposal. A future
Trivy scan may fetch a fresh database; exact regeneration of this historical
database is not promised.

## Approval boundary

The current adopted storage policy has `cleanupControllers: []`. Andrew Spiteri
specifically approved this exact two-file removal. Both hashes and drainage were
rechecked before removal; all 686 remaining security files matched their retained
hashes afterward. The cleanup manifest records the actual operation. No general
cleanup controller, resource increase or gate exception was added.

Local evidence is retained under
`.cis/local/testing/current-context-closeout/`, including
`trivy-cache-drain-check.json` and the resource inspection receipts. Scanner
acquisition still needs a fresh conservative peak admission after cleanup.

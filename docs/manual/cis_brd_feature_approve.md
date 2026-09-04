---
title: "cis brd feature approve"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-22"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-brd-feature-approve
---

# `cis brd feature approve`

```text
cis brd feature approve --item <HLT-ID> --reviewer <human>
  --reason <rationale> [--workspace <path>] [--format <human|json|agent>]
```

Records explicit human approval of a valid, current backlog-derived feature
specification. A new approval requires an Active/current high-level backlog and writes
the reviewer, UTC timestamp, rationale, and approved-content digest before promoting the
document and catalog entry to Active. Repeating the exact approval is idempotent.

An edited previously Active feature reports `Stale` because its content no longer
matches the prior approval digest. CIS excludes that unapproved revision from BRD
evidence, preventing it from invalidating its own upstream approval gate. With explicit
human authority, `feature approve` may renew the digest while the feature is structurally
valid and the unchanged high-level backlog remains Active/current. The renewed feature
then re-enters BRD evidence and requires ordinary graph rebuild and BRD reconciliation.

An Active feature becomes eligible BRD evidence. Rebuild the graph, run `cis brd
reconcile`, assess the source, and refresh technical intent before change planning.
Evidence-only reconciliation of a current human-approved authority feature reuses that
approval and does not require another BRD review or revoke the semantic product-definition
baseline. A real edit to business intent still requires normal review.

The VS Code delivery loop performs this mechanical reconciliation and refresh immediately
after successful feature approval. When no change is active, it creates one change dossier
rooted at the feature, runs deterministic impact analysis followed by `cis plan derive`
against the exact approved feature, and opens the resulting delivery
workspace. Feature approval accepts scope; it does not itself authorize implementation,
deployment, or release. Only run it with explicit human authority.

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
reconcile`, assess the source, and renew BRD, technical-intent, and backlog approvals as
needed before change planning. Feature approval accepts scope; it does not authorize
implementation, deployment, or release. Only run it with explicit human authority.

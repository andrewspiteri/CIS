---
title: "cis artifacts plan"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-10-26"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-artifacts-plan
---

# `cis artifacts plan`

Preview retain, archive, and delete decisions without changing local state.

```text
cis artifacts plan [--family <name>] [--repo <path>] [--format <human|json|agent>]
```

The newest `Keep latest` entries remain protected. Other entries become candidates when older than the configured age. With an optional `Maximum bytes` budget, the oldest unprotected entries also become candidates before that age when needed to reduce projected live usage. Existing five-column profiles keep age-only behavior. Preserve families never gain candidates.

The plan reports each family's live, protected, eligible and projected live bytes. If protected entries alone exceed the budget, it reports an error and cleanup remains blocked until the policy is reviewed. A successful plan makes no changes and does not enforce a write quota. Review it after active writers finish; use a separate confirmed `compact` or `clean` command to apply eligible actions.

The mutation default covers `.cis/local/testing/mutation/runs`: one day, two newest runs protected, delete policy and 512MiB live budget. Retain the small native report and assessment outside transient `runs` before cleanup. Delete-policy hashes cannot restore removed bytes.

JSON output includes `familyStorage` and `storage`; human and agent output show the same byte totals. `managementBytes` includes archive ZIPs, manifests, tombstones and retrieved copies. `archiveBytes` is a subset of management, and `totalReportedBytes` adds configured live and management bytes once. Unconfigured paths and scratch space elsewhere are excluded. Moving live data into an archive is not proof that total disk usage is bounded.

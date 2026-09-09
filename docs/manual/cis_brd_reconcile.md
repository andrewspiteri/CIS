---
title: "cis brd reconcile"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-20"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-brd-reconcile
---

# `cis brd reconcile`

Reconciles new or changed BRD and development feature-specification evidence into the
canonical BRD review workflow. It requires an existing canonical BRD; use `brd init`
for initial creation.

## Synopsis

```text
cis brd reconcile [--workspace <path>] [--format <human|json|agent>]
```

The command refreshes participant graph baselines and the managed source-assessment
table. New, unapproved, stale, or digest-invalid feature specifications enter as
`Unreviewed`. A current `Active` feature specification with valid human approval
metadata and a matching approval digest is carried forward as `Adopted`, using its
reviewer and approval rationale, and receives a managed `BRD-SRC-*` traceability entry.
An existing explicit `Reference` or `Rejected` assessment is never replaced by feature
approval. Unchanged assessments and all human-authored BRD sections are preserved. When
the BRD already has a current human approval and reconciliation only adds fully resolved
managed evidence, the command preserves that approval and refreshes its semantic digest.
Unreviewed evidence or a material change to human-authored business requirements clears
the prior approval and returns the BRD to `Review Required`.

Automatic adoption reuses an explicit feature approval; it does not invent product
decisions or copy technical detail into business requirements. If the approved feature
adds or changes business intent, a human must still incorporate that delta into the
relevant BRD sections before reapproval. The managed traceability entry proves which
approved feature supplied the evidence.

## Development workflow

Reconciliation retains managed baseline, source-assessment and adopted-feature traceability
data inside reversible HTML comments. Standard metadata headings are hidden too. Source
values and assessments remain available to CIS and Markdown-source readers. Moving this
controller-owned metadata into comments preserves the business-content digest and does
not require renewed approval by itself. Human-authored narrative and evidence comments
are preserved.

1. Generate or update a feature specification with front matter
   `type: feature-specification`.
2. Rebuild the affected repository graph.
3. Run `cis brd status`, then `cis brd reconcile` when evidence is unresolved.
4. Review any unassessed evidence and update BRD business sections for accepted product
   deltas. Current approved features already carry their assessment and traceability.
5. Request explicit human re-approval only when business intent changed or unresolved
   evidence remains. Evidence-only traceability updates retain a current approval.

## Exit codes

| Code | Meaning |
| ---: | --- |
| `0` | Evidence reconciled or already unchanged. |
| `2` | Workspace, authority, or graph evidence is invalid. |
| `4` | The canonical BRD is missing. |
| `5` | The reconciled BRD still has review gaps. |

## Related commands

- [`cis brd discover`](cis_brd_discover.md)
- [`cis brd validate`](cis_brd_validate.md)
- [`cis brd approve`](cis_brd_approve.md)

---
title: "cis brd backlog start"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-04"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-brd-backlog-start
---

# `cis brd backlog start`

```text
cis brd backlog start --item <HLT-ID> [--slug <feature-slug>]
  [--workspace <path>] [--format <human|json|agent>]
```

Starts one dependency-ready feature specification from an Active/current high-level
backlog. When the high-level product-definition wizard has been used, its complete
baseline must also have received the final consolidated activation. The default canonical path is
`<authority-documentation-root>/specs/features/<lowercase-HLT-ID>/feature-specification.md`.
`--slug` may select a stable lowercase directory name before the feature is started.

The command:

1. verifies the backlog and selected item;
2. blocks while any declared dependency still has `featureSpec=not-created`;
3. creates a Draft workspace feature specification with BRD, technical-intent, backlog,
   complete product-definition digest, requirement, target-repository, and acceptance-intent provenance;
4. registers the document in the authority catalog; and
5. records its canonical path in the backlog row.

The generated file is deliberately a schema-complete scaffold, not a finished feature.
Immediately run `cis agent author feature --item <HLT-ID> --provider <provider>
--actor <human>` (or use the VS Code **Draft with agent** action) to expand it from the
current governed baseline. CIS applies only a protected one-file result with no remaining
template placeholders. Manual authoring remains supported.

The managed link update does not revoke the human-approved outcome or dependency graph.
Other backlog edits still invalidate approval. A backlog-derived feature remains downstream
Draft work and is not offered for BRD absorption until it leaves Draft. Rerunning with the
same item and path is idempotent; a different existing identity or link is a collision.

Exit `0` means started or unchanged, `2` means invalid input or a collision, `4` means the
item is missing, and `5` means backlog authority or feature dependencies are not ready.

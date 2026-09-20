---
title: "cis definition activate"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-15"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-definition-activate
---

# `cis definition activate`

```text
cis definition activate --reviewer <human> [--workspace <path>] [--format <human|json|agent>]
```

Records one human decision over the exact complete high-level baseline. It activates the BRD,
technical intent, solution-design bundle, UI direction, diagrams, dictionary index, UI preview,
and high-level backlog, then rebuilds the graph. The fixed reason is recorded by CIS; no separate
rationale option exists.

Activation binds the reviewed architecture diagrams to the approved design and component sheet
before signing the diagram set. Recording approval metadata keeps those views current. Status
also recognizes an unchanged baseline from earlier activations when its diagrams still match the
current renderer; it preserves the existing approval and does not rewrite the documents.

Activation records one digest over the complete high-level baseline. The dictionary inventory is
part of that baseline while each feature-authoring run separately binds the exact dictionary
contents it consumed. Managed feature links do not change the high-level digest. Feature start, agent authoring,
validation, and approval consume that digest. Any later material change to a baseline artifact
makes the binding stale and returns the feature loop to reconciliation instead of offering a
misleading approval.

Every page must first be valid, complete, and current. Canonical files are snapshotted before the
operation and restored if a bounded lifecycle or I/O failure occurs, preventing partial success.

---
title: "cis change rebaseline"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-20"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-change-rebaseline
---

# `cis change rebaseline`

Adopts the current exact Git and graph baseline for an existing open dossier and
records who authorized the change and why.

```text
cis change rebaseline <change-id> --actor <identity> --reason <rationale>
  [--repo <path>] [--format <human|json|agent>]
```

The command is intentionally limited to the pre-impact stage: `impact.md` must contain
no findings and `plan.md` must contain no generated work items. It updates the managed
baseline fields in `proposal.md`, `impact.md`, and `plan.md`, then appends a
`change-rebaselined` event containing the previous and new identities, actor, and
rationale. It never rewrites reviewed impact, task, approval, or completion evidence.

Normal creation of or edits to `changes/CIS-NNNN/` do not require rebaseline. Managed
change-dossier documents and their catalog entries remain graph content but are
excluded from the product/source graph fingerprint, preventing CIS records from
self-invalidating their own baseline. Use this command only when genuine source or
governance inputs changed before impact review.

Exit `0` means applied or already current. Exit `2` means the dossier, graph, actor,
reason, or safe pre-impact condition is invalid.

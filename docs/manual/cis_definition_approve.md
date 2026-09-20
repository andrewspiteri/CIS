---
title: "cis definition approve"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-15"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-definition-approve
---

# `cis definition approve`

```text
cis definition approve --page architecture --reviewer <human> [--workspace <path>] [--format <human|json|agent>]
```

Approves the overall solution design, component sheet and high-level architecture diagram set
together. Business requirements and technical intent must already be Active and current; this
command never approves them implicitly. The design must validate, and its diagrams must exist
and match the current sources. The wizard places BRD approval in Business definition and technical
intent approval in Technical direction. The architecture step links back to any unfinished
prerequisites and displays the remaining validation warnings before enabling its own approval.

CIS records the reviewer and a fixed wizard approval reason. It rebinds the reviewed diagrams to
the source documents' approval metadata and activates their catalogue entry. If approval or the
following graph build fails, the architecture documents and catalogue are restored together.
The remaining definition pages and final product activation retain their existing gates.

Exit 0 indicates success, 5 indicates validation or operation failure, and 2 rejects an unsupported
page. Structured output returns the current wizard pages, diagrams, errors and `applied` state.

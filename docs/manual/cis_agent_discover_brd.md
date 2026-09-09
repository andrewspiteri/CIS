---
title: "cis agent discover brd"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-07"
review_cadence: "on command or evidence-policy change"
cis:
  stable_id: change-impact-studio:manual:cis-agent-discover-brd
---

# `cis agent discover brd`

Prepares a local preview of implementation and test evidence for selected product-owned
repositories. It does not contact an agent provider or change the canonical BRD.

```text
cis agent discover brd --reference <initialized-owned-repository>...
  --actor <human> [--repo <authority>] [--format <human|json|agent>]
```

Discovery first prepares observed dictionaries in owned repositories and consolidates them in
the authority with repository attribution, then refreshes the graphs. This also runs during
direct authoring; a separate wizard preparation is optional. Discovery registers the explicitly selected repository references,
builds their local projections, and prepares cached code snapshots with file inventories,
content digests, line counts, coverage areas, redaction flags and omissions.

The result's envelope and `.cis/local/agents/implementation/selection.json` identify the
snapshot directories. Open each `INDEX.md` and `manifest.json`; the adjacent `files/`
directory contains the exact redacted source text. Limits and exclusions are described in
`cis agent author brd`. A warm discovery verifies and reuses unchanged snapshot files.

This preview authorizes no disclosure. Select and authorize the destination provider when
running `cis agent author brd`; that command verifies/prepares current snapshots again and
uses the same coverage contract. Including authoring evidence in an independent review also
shares those implementation/test snapshots with the review provider.

Discovery and authoring share the product BRD lock so preparation cannot overlap an active
authoring run. A linked path, changed or missing evidence, or unavailable owned-repository
boundary is reported without substituting another repository or provider.

---
title: "cis design render"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-13"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-design-render
---

# `cis design render`

Runs the canonical Sharp/SVG renderer and creates the PNG review pack.

```text
cis design render <change-id> [--repo <path>] [--format <human|json|agent>]
```

Static validation runs first. CIS records Node, Sharp, and libvips versions plus each
PNG path, dimensions, bytes, and SHA-256 hash. Success sets `gate_status` to
`PausedForReview`; all non-review work must stop until explicit design approval.

CIS resolves the Sharp runtime without requiring the documentation authority to own
a Node project. It checks `CIS_SHARP_NODE_MODULES`, the renderer and authority
ancestors, registered workspace participants, and finally `NODE_PATH`. This allows a
workspace design renderer to reuse the governed frontend repository's pinned Sharp
installation while keeping the renderer and PNG evidence in the authority repository.

Only screens not already satisfied through `cis design reuse` are present in the
renderer. The review manifest and its SHA-256 combine those new PNGs with all verified
reused PNGs, so approval covers the complete screen set without regenerating unchanged
visuals.

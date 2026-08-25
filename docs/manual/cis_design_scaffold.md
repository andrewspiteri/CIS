---
title: "cis design scaffold"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-23"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-design-scaffold
---

# `cis design scaffold`

Generates a self-contained Sharp/SVG JavaScript renderer from structurally valid textual
wireframes.

```text
cis design scaffold <change-id> --feature <name> [--shell <template-id>] [--component <template-id>...] [--force] [--repo <path>] [--format <human|json|agent>]
```

The command requires valid `wireframes.md` and a governed design-guidelines document.
Every screen is placed inside the selected application shell. With no component options,
CIS selects its common page, filter, table, status, card, and empty-state components.
It records template versions and source hashes in `design.md`. Existing renderers are
protected unless `--force` is explicitly supplied after review.

A separate wireframe approval is optional. By default the human reviews behavior and
rendered appearance together, and `cis design approve` records the exact wireframe
digest alongside the renderer and PNG manifest in one decision.

Template dependencies are resolved transitively. For example, a table automatically
records its button and status-badge dependencies; a form records its button, text-input,
select, text-area, and checkbox dependencies. The renderer provenance therefore lists
every common component actually used, including shell-owned controls.

---
title: "cis ui-direction baseline"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-14"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-ui-direction-baseline
---

# `cis ui-direction baseline`

```text
cis ui-direction baseline [--workspace <path>] [--format <human|json|agent>]
```

Discovers the existing web interfaces in product-owned repositories. The high-level
wizard invokes this automatically when its Experience page opens and when that page
refreshes. Other wizard pages and startup snapshots do not run UI discovery.

The result describes framework dependencies, shared shells and component selectors,
theme configuration, color declarations, typography, layout declarations, media queries,
feedback controls and accessibility markers. Each observation includes a repository-relative
file and line for review. Route fragments remain local declarations; nested and lazy route
prefixes are not resolved into global URLs. Separate repositories retain separate baselines.

Each repository also reports observed control families with source locations and a deterministic
SVG contact sheet in `preview` (`svg`, `width`, `height`, `description`). The Experience page
automatically renders that sheet to JPG in the webview and displays it before implementation
details. **Save JPG control sheet** exports the image and opens it beside the wizard. A render
failure retains the SVG with an explicit retry message. No additional package or app build is needed.

These are source-based reference specimens, not screenshots of executed product components.
Only detected families appear: supported patterns include buttons, fields, dropdowns, choices,
tables, cards, tabs, dialogs and feedback. Observed primary colors and font declarations inform
the render; unresolved colors use a labelled neutral palette. Example content, geometry and
states use reference defaults. Font availability, exact framework styling and runtime behavior
must be checked against the running interface. Exporting a sheet does not approve UI direction.

Discovery prioritizes shared styles, tokens, layout, configuration and navigation. Per
repository, it reads at most 192 eligible files, 128 KiB per file and 2 MiB overall, with
a 12,000-entry enumeration limit. Results report sampling limits. Dependency repositories,
documentation, generated trees, test fixtures, environment files and reparse points are
excluded. No packages, applications or model providers are executed.

The disposable cache is `.cis/local/ui-baseline/baseline.json`. Every invocation checks the
selected file contents before reuse, including edits that preserve timestamps and length.
Unreadable repositories and limits remain visible. This command does not change canonical
documents, record answers or approve a design. Observations are not runtime screenshots,
proof of accessibility compliance or a complete account of uninspected behavior.

Questionnaire initialization uses these observations as advisory starting directions when
available. Existing human answers are preserved. Product character, accessibility targets,
brand ownership and unobserved constraints require human review. The wizard permits baseline
inspection before upstream decisions are complete; recording and generating governed UI
direction still requires the existing architecture gate (or a current, validated draft
inside the high-level wizard). The wizard initializes those choices automatically once
that prerequisite is ready.

JSON includes `status`, `workspacePath`, `sourceHash`, `cached`, `repositories`,
`suggestions`, `warnings`, `errors`, and `exitCode`. Exit 0 means discovery completed,
including `not-found` when no supported interface was found; exit 5 reports invalid
workspace authority. Unsupported output formats exit 2.

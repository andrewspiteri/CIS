---
title: "cis design templates"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-13"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-design-templates
---

# `cis design templates`

Lists versioned reusable application-shell and component templates.

```text
cis design templates [--format <human|json|agent>]
```

Each result includes kind, version, purpose, and estimated possible token savings.
The built-in default is `shell.standard-app`, which provides persistent navigation for
multi-surface applications. Select `shell.minimal-app` for simple, single-surface
products that need only a header, route context, and content canvas. The component system includes buttons,
text inputs, selects/dropdowns, text areas, checkboxes, radio groups, toggles, tabs,
breadcrumbs, pagination, action menus, dialogs, alerts, tables, filters, badges, cards,
forms, empty states, timelines, accordions, and avatars. Each definition includes its
states and interaction responsibilities; a table is only one reusable component.
Composite templates resolve their component dependencies automatically so features do
not need to repeat or manually synchronize the same lower-level controls.

---
title: "Design Guidelines Template"
type: template
status: Active
scope: Repository
owner: Repository maintainer
review_cadence: on design-system change
cis:
  stable_id: change-impact-studio:template:design-guidelines
---

# Design guidelines

Copy this template to the repository's governed specification area, change its
front matter to `type: design-guidelines` and `status: Draft`, give it a unique stable
ID, and replace every `TODO`. Approve it before using it as the authority for a visual
design task.

## 1. Brand and product intent

The product should feel:

- **TODO quality**: TODO explanation
- **TODO quality**: TODO explanation
- **TODO quality**: TODO explanation

These guidelines apply to:

- TODO: web application, native application, administration UI, documents, diagrams,
  or other governed surfaces

## 2. Core visual direction

Aim for:

- TODO

Avoid:

- TODO

## 3. Color system

Use a restrained palette. Give every color a semantic role rather than using color
only as decoration.

| Token | Value | Required usage |
| --- | --- | --- |
| `color.bg.primary` | TODO | TODO |
| `color.bg.soft` | TODO | TODO |
| `color.bg.dark` | TODO | TODO |
| `color.text.primary` | TODO | TODO |
| `color.text.secondary` | TODO | TODO |
| `color.text.muted` | TODO | TODO |
| `color.border` | TODO | TODO |
| `color.accent.primary` | TODO | TODO |
| `color.accent.secondary` | TODO | TODO |
| `color.status.success` | TODO | TODO |
| `color.status.warning` | TODO | TODO |
| `color.status.critical` | TODO | TODO |

Palette balance and contrast rules:

- TODO: neutral/dark/accent proportions
- TODO: minimum text and component contrast
- TODO: prohibited color combinations or color-only state indicators

## 4. Typography

| Role | Family | Size/scale | Weight | Line height |
| --- | --- | --- | --- | --- |
| Display/hero | TODO | TODO | TODO | TODO |
| H1 | TODO | TODO | TODO | TODO |
| H2 | TODO | TODO | TODO | TODO |
| H3/card heading | TODO | TODO | TODO | TODO |
| Body | TODO | TODO | TODO | TODO |
| Meta/caption | TODO | TODO | TODO | TODO |

- Primary font: TODO
- Permitted fallback: TODO
- Renderer embedding/licensing rule: TODO
- Heading case and copy style: TODO
- Prohibited weights/styles: TODO

## 5. Tone expressed visually

The visual design should communicate:

- TODO

Translate that intent through:

- TODO: spacing, hierarchy, contrast, density, or other design choices

## 6. Layout and spacing system

- Grid: TODO
- Base spacing unit: TODO
- Permitted spacing tokens: TODO
- Desktop/content width and margins: TODO
- Mobile margins and breakpoint behavior: TODO
- Density rule: TODO
- Maximum competing highlights per screen: TODO

## 7. Content hierarchy and copy

- Heading rule: TODO
- Body-copy rule: TODO
- Label and helper-text rule: TODO
- Empty/error/denied-state tone: TODO
- Truncation, wrapping, and overflow policy: TODO
- Terms or filler language to avoid: TODO

## 8. Component rules

Define visual and interaction rules for applicable components:

| Component | Required style and states |
| --- | --- |
| Buttons and links | TODO |
| Text inputs, text areas, and validation | TODO |
| Selects/dropdowns and action menus | TODO |
| Checkboxes, radio groups, and toggles | TODO |
| Cards and panels | TODO |
| Tables and lists | TODO |
| Tabs, breadcrumbs, navigation, and pagination | TODO |
| Dialogs/drawers | TODO |
| Alerts, status badges, and callouts | TODO |
| Accordions and disclosure | TODO |
| Avatars and identity | TODO |
| Empty, loading, error, denied, and recovery states | TODO |
| Timelines/activity | TODO |
| Charts and diagrams | TODO |

## 9. Icons, imagery, charts, and diagrams

- Icon family/style: TODO
- Image usage: TODO
- Prohibited imagery: TODO
- Chart palette and annotation rules: TODO
- Diagram shapes, connectors, labels, and zone rules: TODO
- Prohibited chart/diagram styles: TODO

## 10. Responsive and platform behavior

| Viewport/platform | Width or class | Layout behavior | Navigation behavior |
| --- | --- | --- | --- |
| TODO | TODO | TODO | TODO |

Document platform conventions that override a shared visual rule and explain why.

## 11. Accessibility rules

- Contrast: TODO
- Focus visibility: TODO
- Keyboard/order intent: TODO
- Target sizes: TODO
- Error and state communication: TODO
- Reduced motion: TODO
- Text scaling/zoom: TODO

## 12. Consistency rules

Always keep these fixed unless a recorded design decision approves a deviation:

- TODO: primary family, core palette, heading system, spacing rhythm, component style,
  sentence case, or other system invariants

For different platforms or density modes, state what may change and what remains
visually invariant.

## 13. Design tokens

```text
TODO: canonical color, spacing, radius, border, typography, and shadow tokens
```

## 14. Reference screens and patterns

| Pattern | Canonical source | What to reuse | What not to copy |
| --- | --- | --- | --- |
| TODO | TODO | TODO | TODO |

## 15. Final style summary

An on-brand screen should feel:

- TODO

It should never feel:

- TODO

## 16. Working defaults

- Font: TODO
- Background: TODO
- Primary text: TODO
- Primary accent: TODO
- Supporting accent: TODO
- Heading style: TODO
- Body style: TODO
- Layout: TODO
- Visual style: TODO

## 17. Approval and deviations

| Decision | Reviewer | Date | Guideline digest | Rationale |
| --- | --- | --- | --- | --- |
| Not reviewed | TODO | TODO | TODO | TODO |

Every feature-specific deviation records the affected rule, reason, approving human,
date, and the task/design artifacts to which the exception applies.

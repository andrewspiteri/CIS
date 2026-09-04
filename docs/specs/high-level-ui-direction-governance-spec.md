---
title: "High-Level UI Direction Governance Specification"
type: specification
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-03"
review_cadence: "on product-definition workflow change"
cis:
  stable_id: change-impact-studio:spec:high-level-ui-direction-governance
---

# High-Level UI Direction Governance Specification

## Purpose

Define the product-facing experience stage between approved solution architecture and the
high-level backlog. It gives every UI-bearing feature one shared look-and-feel authority without
prematurely producing feature screens or treating a component framework as product design.

## Workflow position

```text
Active BRD
  -> Active technical intent
  -> Active overall solution design + component sheet
  -> Complete UI-direction questionnaire
  -> Active high-level UI direction
  -> High-level backlog
  -> feature specification -> textual wireframe -> rendered design pack
```

Backlog, change, and planning commands fail closed when the required UI direction is missing,
incomplete, stale, invalid, or unapproved. Mechanical revalidation creates no extra human gate.

## Canonical artifacts

- `specs/ui-direction-questionnaire.md` records twelve stable `UI-Q-*` decisions.
- `design/ui-direction.md` records the approved workspace-level UI authority.
- `specs/design-guidelines.md` provides the active default or repository-specific visual rules.
- `references/ui-framework-profile.md` records evidence-first framework selection per UI component.

Markdown remains canonical. Derived frontend graphs and indexes remain under `.cis/local/`.

## Questionnaire model

The questionnaire covers surfaces and audiences, product character, shell/navigation, layout and
density, color/theme, typography/content, reusable components, responsive adaptation,
accessibility/input, state/feedback/motion, recurring data/action patterns, and constraints.

For an existing product, CIS may derive only objective choices supported by approved technical
surfaces or an indexed UI-framework profile. A human may edit a derived answer. Subjective brand,
character, shell, density, and experience choices never become authority merely because a code
marker or model suggestion exists. Greenfield projects receive advisory starting directions and
must record every answer explicitly.

## High-level direction contract

`cis ui-direction init` requires an Active/current solution-design bundle and a Complete/current
questionnaire. The generated document must cover:

- experience intent and product surfaces;
- shell and navigation;
- visual tokens, layout, typography, content, iconography, and imagery;
- reusable component and recurring data/action patterns;
- state, feedback, motion, responsive behavior, and platform adaptation;
- accessibility and inclusive input;
- constraints, exclusions, feature handoff, and exact source traceability.

The artifact defines common direction only. It does not invent feature scope, screen-specific
actions, routes, or state transitions. Those belong to textual feature wireframes. PNG review
remains the deterministic Sharp/SVG feature-design gate.

## Reuse and framework rules

An evidenced existing design system wins. Otherwise the repository's classification-selected UI
framework profile provides the default implementation primitives. Styling infrastructure alone
does not establish a component system. Shared shell and common controls include focus, validation,
loading, disabled, denied, empty, error, and recovery behavior; feature renderers consume their
governed templates rather than redrawing them.

## Lifecycle and drift

`cis ui-direction validate` checks structure, all `UI-Q-*` links, source hashes, design-guideline
and UI-framework-profile provenance, and approval integrity. `cis ui-direction approve` records
explicit human authority over the exact content digest. Changes to the solution bundle,
questionnaire, design guidelines, UI-framework profiles, or approved content make the document
Stale. Reinitialization preserves content outside the managed block and never restores approval.

## Commands

```text
cis ui-direction questions init|status
cis ui-direction questions answer <UI-Q-ID> --answer <text> --actor <human>
cis ui-direction init|validate|status
cis ui-direction approve --reviewer <human> --reason <rationale>
```

All commands support workspace selection and human, JSON, or compact agent output.

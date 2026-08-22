---
title: "Task Type: Frontend Implementation"
type: task-type-definition
status: Draft
version: "0.1"
scope: "Product:ChangeImpactStudio"
owner: "Andrew Spiteri"
last_reviewed: "2026-08-13"
review_cadence: "on planning-model change"
cis:
  stable_id: change-impact-studio:task-type:core.frontend.implementation
---

# Task Type: Frontend Implementation

## Identity and boundary

| Property | Value |
| --- | --- |
| Stable type key | `core.frontend.implementation` |
| Provider | CIS Plan core module |
| Creation policy | Conditional |
| Required predecessor(s) | Approved Visual Design; API and Security where applicable |
| Primary consumer(s) | Integration, verification, accessibility assurance, and final delivery |

Implement the approved textual behavior and visual screen pack using the repository's existing or resolved UI framework, shell, and reusable component system.

Each task instance is classified as exactly `public`, `customer`, or `backoffice`.
Planning creates separate instances when multiple types are affected so their routes,
actors, permission boundaries, shell context, validation, and evidence cannot be
silently merged.

## Activation and inputs

### Creation evidence

- Web, mobile, native, route, screen, page, view, tab, user-interface, or frontend evidence.

### Required inputs

- Approved wireframe and design digests, PNG manifest, UI-framework profile, design guidelines, contracts, permissions, and responsive/accessibility requirements.
- The matching frontend type and only the requirements/screens assigned to it.

## Required activities

1. Map each approved screen/state/action to production routes and components.
2. Reuse the application shell and governed components; extend shared patterns instead of redrawing equivalents.
3. Implement loading, empty, invalid, error, denied, read-only, responsive, and lifecycle states.
4. Preserve visible permissions, navigation destinations, side effects, keyboard/focus, semantics, and platform conventions.
5. Compare implementation with approved PNGs and run component, interaction, accessibility, build, and journey checks.

## Required outputs, dependencies, and authority

- Production UI implementation using the approved framework and component system.
- Screen/state/action traceability to wireframes and PNG manifest.
- Visual-fidelity, interaction, accessibility, and build evidence.

Only tasks without an unresolved dependency may run concurrently. Approved Visual
Design is a direct global dependency for every downstream UI-bearing task. Human
authority is required for scope changes, deferrals, residual-risk acceptance, and
any repository policy exception; task execution and evidence collection do not imply
that authority.

## Acceptance criteria

- [ ] Every approved screen, state, action, path, and viewport behaves as specified and follows the approved visual direction.
- [ ] No unapproved control, route, exposure, component system, or alternate interaction is introduced.
- [ ] Targeted UI, accessibility, type/lint/build, and browser/platform checks pass.

## Negative criteria

- Do not start before exact design approval or continue during a global design pause.
- Do not introduce a second UI framework to match a default.
- Do not hide missing backend/security behavior behind client-only logic.

## Validation and completion evidence

- Run component/interaction tests, accessibility scans, lint/type/build, responsive visual comparison, and critical user journeys.
- Record screen/PNG mapping, commands, results, deviations, and approved residual risk.

Completion evidence must identify the exact baseline, affected target, command or
review method, timestamp, actor/tool version, result, and artifact path or digest.
Open placeholders, unchecked acceptance/validation items, unexplained warnings, or
missing evidence prevent completion.

## Deferral and complexity

Any deferral records the unmet criterion, reason, owner, consequence, revisit
condition, residual risk, and human approval. Low for one established screen; medium for several states/routes; high for multi-platform, accessibility-critical, or novel interaction work and must be decomposed.

## External issue hints

- Title: `Frontend Implementation: <feature title>`.
- Labels: `cis`, `task-type:core.frontend.implementation`, plus affected repository/component labels.
- Body: canonical task link, source digest, targets, dependencies, gates, acceptance,
  validation, and evidence expectations.
- External status never grants CIS approval, accepts risk, or replaces the canonical
  Markdown task lifecycle.

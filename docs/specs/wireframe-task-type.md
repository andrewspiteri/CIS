---
title: "Task Type: Wireframe"
type: task-type-definition
status: Draft
version: "0.1"
scope: "Product:ChangeImpactStudio"
owner: "Andrew Spiteri"
last_reviewed: "2026-08-13"
review_cadence: "on planning-model change"
cis:
  stable_id: change-impact-studio:task-type:core.design.wireframe
---

# Task Type: Wireframe

## 1. Identity

| Property | Value |
| --- | --- |
| Stable type key | `core.design.wireframe` |
| Provider | CIS Plan core module |
| Creation policy | Conditional; create when a feature changes a human-facing screen, view, route, navigation flow, or interactive state |
| Required predecessor | `core.coordination.scope-guard` |
| Primary consumer | `core.design.visual` |
| Canonical output | `changes/<change-id>/wireframes.md` |

## 2. Purpose and boundary

The Wireframe task turns approved feature behavior into a textual screen and
navigation contract. It describes what each screen contains, what a user can do,
under which conditions an action is available, and where that action leads.

The wireframe is deliberately textual. It defines information architecture,
behavior, states, and paths without choosing colors, typography, spacing, imagery,
or production component implementation. It must be detailed enough that the Visual
and interaction design task can render the screen without inventing product behavior.

## 3. Creation triggers

Create the task when accepted scope introduces or changes any of the following:

- web, desktop, mobile, native, game, editor, or administration screens;
- routes, tabs, drawers, dialogs, panels, navigation destinations, or deep links;
- visible fields, actions, filters, tables, timelines, forms, or state messages;
- user journeys, role-dependent behavior, or responsive interaction; or
- empty, loading, error, denied, validation, lifecycle, or destructive-action states.

Do not create it for backend-only changes with no human-facing behavior. If trigger
evidence is mixed, create the task as Draft and record the decision required rather
than silently omitting UI work.

## 4. Inputs

The task consumes:

- approved feature goals, requirements, exclusions, actors, and scenarios;
- accepted UI, navigation, permission, lifecycle, and integration impacts;
- existing screen/route maps and applicable product design patterns;
- affected platform and viewport classes;
- relevant permissions, feature flags, and workflow states; and
- decisions that constrain entry points, interaction patterns, or destinations.

## 5. Textual wireframe format

`wireframes.md` contains one section per screen or materially different screen state.
Every screen definition uses this structure:

### 5.1 Screen identity

| Field | Meaning |
| --- | --- |
| Screen ID | Stable feature-local identity used by actions and design assets. |
| Frontend type | Exactly `public`, `customer`, or `backoffice`; determines the governed UI task chain and shell context. |
| Name | User-facing screen or state name. |
| Route/path | Concrete application path, route pattern, deep link, or `not-applicable` for a non-routed surface. |
| Platform | Web, iOS, Android, desktop, game/editor, or another classified client. |
| Actors/access | Roles that may view the screen and the denied behavior for others. |
| Entry points | Paths and actions from which users reach the screen. |
| Purpose | User outcome achieved on the screen. |

### 5.2 Screen description

The description states, in reading order:

- shell and navigation context;
- headings, summaries, status or visibility indicators;
- content regions and the information each displays;
- fields, controls, defaults, validation, and conditional visibility;
- primary, secondary, destructive, and escape actions; and
- responsive or platform-specific rearrangement.

Screen IDs remain unique across all three frontend types. A feature spanning more than
one type keeps the screens in the same canonical `wireframes.md`, but each screen and
each generated wireframe task is explicitly classified; approval covers the exact
classified inventory digest.

### 5.3 Actions and paths

Each interactive action is represented explicitly:

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |

`Destination path` is mandatory for navigation. Non-navigation actions use one of
`same-screen`, `dialog:<screen-id>`, `drawer:<screen-id>`, `external:<approved-url>`,
or `not-applicable`. Dynamic paths show parameters, for example
`/leads/{leadId}/research/{researchId}`. An action may not use vague destinations
such as "details page" without a path or stable screen ID.

Side effects identify the command, API operation, state transition, download, or
other behavior when known. The wireframe does not invent a contract that is absent
from the approved feature scope.

### 5.4 Required states

Each screen declares applicable states and explains visible differences:

- populated/primary;
- empty;
- loading or pending;
- validation failure;
- recoverable and non-recoverable error;
- unauthenticated or permission denied;
- read-only, disabled, archived, or other lifecycle state;
- destructive-action confirmation and result;
- responsive or platform-size variants; and
- accessibility behavior that affects structure or interaction.

`Not applicable` requires a reason. Different states may be separate screen sections
when their layout or available actions materially differ.

## 6. Required activities

The task owner must:

1. Inventory affected screens, routes, entry points, actors, and workflow states.
2. Give every screen and action a stable identity.
3. Describe every screen in reading order using the canonical format.
4. Map every action to its condition, result, and destination path or explicit
   non-navigation value.
5. Trace primary, alternate, cancel, retry, denied, and destructive journeys end to
   end; ensure each destination resolves to a screen or approved external target.
6. Cover responsive, accessibility, permission, feature-flag, and lifecycle behavior.
7. Map UI requirements, exclusions, and prohibited behavior to screens and actions.
8. Compare the proposed flow with existing routes and interaction patterns.
9. Resolve product ambiguity through decisions rather than visual-design guesses.
10. Present the textual wireframe for human behavioral and navigation review.

## 7. Required outputs

- A catalogued `wireframes.md` document.
- Screen inventory and textual description for every required state.
- Complete action-and-path tables.
- Journey and requirement coverage mapping.
- Open questions and decisions blocking visual design.
- Human review decision and approved wireframe revision/digest.

## 8. Dependencies and approval gate

Wireframing begins after Coordination / scope guard establishes the feature boundary.
It may proceed alongside documentation, security analysis, and early technical
discovery when it does not consume their unresolved decisions.

Visual design is blocked until a human approves the wireframe's behavior, screen
inventory, actions, and navigation paths. This approval does not approve visual
appearance and does not authorize downstream implementation. After wireframe
approval, Visual design is the next delivery task and becomes the global review
barrier described by `core.design.visual`.

## 9. Acceptance criteria

- [ ] Every affected screen and materially different state has a stable Screen ID.
- [ ] Every routed screen has a concrete static or parameterized path.
- [ ] Every entry point names its source path/action and target screen.
- [ ] Every interactive action records availability, result, destination, and
      failure or denied behavior.
- [ ] Every destination resolves to a defined screen, explicit same-surface target,
      or approved external URL.
- [ ] Primary, alternate, cancel, retry, denied, destructive, and lifecycle journeys
      are complete where applicable.
- [ ] Empty, loading, validation, error, denied, responsive, and accessibility states
      are described or have justified non-applicability.
- [ ] UI requirements and exclusions are traceable to screens, actions, or states.
- [ ] No visual choice is being used to hide unresolved product behavior.
- [ ] A human approved the behavioral/navigation contract and its exact revision.

## 10. Negative criteria

The task must not:

- choose visual branding or production implementation details;
- invent screens, controls, actions, routes, permissions, or side effects outside
  approved scope;
- omit negative, denied, empty, or failure behavior;
- use an unlabeled arrow or prose implication in place of an explicit action path;
- expose restricted data or actions in states where the actor lacks permission;
- treat a PNG, hand sketch, or external design link as the canonical textual contract;
- approve visual design; or
- authorize frontend implementation.

## 11. Validation contract

Deterministic validation checks unique screen/action identities, required fields,
route syntax, destination resolution, entry-point reciprocity, state coverage,
requirement/exclusion coverage, absence of TODO placeholders, source provenance, and
the recorded approval revision.

Human review checks product flow, terminology, information priority, user escape
paths, permission boundaries, and whether a designer could render the screens
without making behavioral decisions.

## 12. Completion evidence

| Evidence | Required content |
| --- | --- |
| Source snapshot | Feature path/digest, accepted impacts, decisions, and route-map revision |
| Wireframe artifact | `wireframes.md` path and SHA-256 digest |
| Coverage report | Requirements, exclusions, screens, states, actions, and journeys |
| Route validation | Exact command/result and unresolved destinations |
| Review | Reviewer, timestamp, decision, rationale, and approved digest |

## 13. Deferral and complexity

A deferred state, actor, or journey records its requirement impact, reason, owner,
revisit condition, and human approval. It cannot be hidden as `not-applicable`.

- `low`: one simple screen with a short, linear action path;
- `medium`: several screens/states, conditional actions, permissions, or responsive
  behavior; and
- `high`: several actors or platforms, branching workflows, many state transitions,
  or unresolved product behavior.

High-complexity wireframing is decomposed by coherent journey, platform, or screen
group while retaining one canonical cross-screen navigation map.

## 14. External issue hints

- Title: `Wireframe: <feature title>`.
- Labels: `cis`, `task-type:wireframe`, and affected platform labels.
- Body: source scope, required screens/states, canonical `wireframes.md` link, review
  gate, and dependencies.
- Completion: approved textual wireframe digest; attached images alone do not close it.

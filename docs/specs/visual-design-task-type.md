---
title: "Task Type: Visual and Interaction Design"
type: task-type-definition
status: Draft
version: "0.2"
scope: "Product:ChangeImpactStudio"
owner: "Andrew Spiteri"
last_reviewed: "2026-08-23"
review_cadence: "on planning-model change"
cis:
  stable_id: change-impact-studio:task-type:core.design.visual
---

# Task Type: Visual and Interaction Design

## 1. Identity

| Property | Value |
| --- | --- |
| Stable type key | `core.design.visual` |
| Provider | CIS Plan core module |
| Creation policy | Conditional; created whenever `core.design.wireframe` is instantiated |
| Required predecessor | Validated `core.design.wireframe` artifact |
| Primary consumer | Frontend implementation task |
| Canonical records | `design.md`, one self-contained JavaScript renderer, and generated PNG files |

## 2. Purpose and boundary

The Visual and interaction design task converts the validated textual wireframe into
a reviewable PNG screen pack. The renderer is the reproducible design source; PNGs
are its generated visual artifacts; `design.md` records provenance, validation, and
human approval.

The task owns layout, visual hierarchy, styling, component appearance, and visible
interaction affordances. It must preserve the approved screens, actions, paths,
states, permissions, terminology, and exclusions. It may not silently redesign
product behavior.

Every design task is classified as `public`, `customer`, or `backoffice` and covers
only screens of that type. When a feature spans types, planning creates one design
task per type while the canonical renderer and `design.md` may consolidate the
classified screen manifests for reproducibility and one global human review barrier.

## 3. Inputs

- Validated `wireframes.md` path and digest, plus an optional earlier review record.
- The task's frontend type and the matching classified screen IDs.
- Feature specification, exclusions, and relevant decisions.
- Approved repository design-guidelines path and SHA-256 digest.
- Existing screen patterns and platform conventions cited by those guidelines.
- Required viewport/platform variants and accessibility intent.
- Representative synthetic data sufficient to show required states.

The renderer must implement the approved guidelines, including their brand intent,
palette, typography, spacing, density, component, responsive, accessibility, and
consistency rules. If guidelines are missing, Draft, incomplete, or conflicting, the
task is blocked until a human approves a guideline revision or explicit deviation.

## 4. Self-contained renderer contract

Each design task produces one repository-owned `.mjs` file that contains:

- the complete screen manifest and output filenames;
- fixed viewport dimensions and variant metadata;
- all layout, style, text, representative data, and screen-state definitions;
- reusable drawing/rendering helpers used by those screens;
- output-directory creation and PNG generation; and
- actionable failure output with a non-zero exit code.

The standard renderer constructs complete SVG documents in JavaScript and converts
them to PNG with the repository's pinned Sharp/libvips version. Repositories may not
substitute Playwright, a browser screenshot, Canvas, or an interactive design tool
for the canonical renderer. All design content and assets live in the JavaScript
source itself. It must not depend on
separate HTML, CSS, JSON, templates, fonts, icons, images, generated source, network
resources, or an interactive design service. Required visual assets are expressed
directly or embedded in the renderer. It must never download resources at render
time.

Running the documented Node command from a clean repository checkout with the pinned
Sharp dependency must regenerate the complete PNG pack without manual steps. The
renderer records or prints its Node, Sharp, and libvips versions.

## 5. Artifact layout

The default feature-local layout is:

```text
changes/<change-id>/
|-- wireframes.md
|-- design.md
`-- assets/
    |-- render-<feature>-screens.mjs
    |-- <screen-id>--<state>--<viewport>.png
    `-- ...
```

Repositories may use a documented equivalent feature folder. Filenames are stable,
lowercase, and derived from Screen ID, state, and viewport. Temporary render files
are excluded from version control and removed or isolated from canonical artifacts.

## 6. Required activities

The designer or agent must:

1. Verify the validated wireframe digest and enumerate every required screen/state.
2. Load the approved design guidelines, record their digest, and map their tokens and
   fixed rules into renderer constants and components.
3. Create the single self-contained renderer and screen manifest.
4. Render every required primary, alternate, empty, loading, error, denied,
   lifecycle, responsive, and accessibility-relevant state.
5. Ensure visible controls, labels, destinations, permissions, and state-aware actions
   match `wireframes.md` exactly.
6. Check palette, typography, spacing, component, density, responsive, and
   accessibility choices against the approved guidelines; record every deviation.
7. Use synthetic, non-sensitive content that demonstrates realistic density,
   wrapping, truncation, validation, and state differences.
8. Run render validation and inspect every PNG at its original dimensions.
9. Reconcile any behavioral ambiguity back into the wireframe/decision process; do
   not resolve it only in JavaScript or pixels.
10. Record guideline path/digest, renderer command, dependency/runtime versions,
    hashes, dimensions, and
   validation results in `design.md`.
11. Present a new or visually changed wireframe digest and PNG pack for one explicit
    human approval. A provenance-only refresh may instead carry current feature
    authority forward when an Approved plan pins the exact feature, the feature
    authority remains current, and the regenerated PNG manifest is byte-for-byte
    identical to the earlier approved manifest. For rejected revisions, retain
    the renderer revision or commit, PNG manifest hashes, reviewer, findings, and
    rationale; remove the rejected PNG files from the canonical asset pack.

## 7. Required outputs

- One self-contained JavaScript renderer.
- One PNG for every required screen/state/viewport combination.
- Updated `design.md` artifact manifest and validation results.
- Traceability from each PNG to Screen ID, state, viewport, wireframe digest, and
  renderer revision.
- Traceability to the approved design-guideline path and digest, plus approved
  deviations.
- Human approval decision naming the exact approved wireframe digest, artifacts, and
  revision, or an explicit authority-carry-forward record naming the approved feature
  and proving the PNG manifest is unchanged.

## 8. Dependencies and approval gate

The task starts after the textual wireframe is structurally valid. It does not run
concurrently with implementation work: for UI-bearing features, the dependency order
begins with Coordination, Wireframe, and Visual design.

When the renderer and PNG pack are ready, the task enters `ReadyForReview`. This is
a global delivery pause. All agents and people stop non-review work for the change;
no documentation, security, data, migration, API, backend, frontend, integration,
infrastructure, operations, rollout, verification, or assurance task may start or
continue until the design review is resolved. Work already executing stops at the
next safe boundary and records its state without beginning another change.

While paused, the only permitted activities are inspecting design evidence,
recording review feedback or the decision, reconciling current approved feature
authority across an unchanged manifest, and revising the wireframe, renderer, or PNG
pack after rejection. Approval or valid authority carry-forward moves the design task
to `Complete` and releases the downstream dependency gate. Rejection returns it to
`InProgress`; the global pause remains until a revised pack is approved.

Plan approval does not imply visual approval. By default visual approval also records
human approval of the exact validated wireframe digest, renderer, and PNG manifest in
one decision; a standalone earlier wireframe approval remains optional. A later material
wireframe or design-guideline change invalidates approval for affected screens,
re-enters the global pause, and requires rerendering. It requires new review when the
rendered manifest changes; otherwise `cis design reconcile` may reuse current feature
authority after all fail-closed provenance checks pass.

## 9. Acceptance criteria

- [ ] Exactly one canonical renderer contains the complete design source and embedded
      assets for the task.
- [ ] The renderer uses JavaScript-generated SVG converted to PNG by pinned Sharp.
- [ ] The renderer runs non-interactively using the documented command.
- [ ] It performs no network access and requires no manual design-tool export.
- [ ] Every required Screen ID, state, and viewport has a manifest entry and
      nonblank PNG output.
- [ ] PNG filenames, dimensions, format signatures, and paths match the manifest.
- [ ] Visible actions, labels, availability, and navigation destinations match the
      validated textual wireframe.
- [ ] No excluded control, upload, destructive action, data exposure, or alternate
      workflow appears in the images.
- [ ] Designs follow applicable repository patterns or explicitly document deviations.
- [ ] Palette, typography, spacing, components, density, responsive behavior, and
      accessibility intent comply with the approved design-guideline digest or have
      explicit human-approved deviations.
- [ ] Representative data exposes realistic overflow and state behavior without
      containing sensitive information.
- [ ] Renderer command, runtime/dependencies, source digest, image digests, dimensions,
      and visual-inspection results are recorded.
- [ ] A human approved the exact renderer revision and PNG set, or CIS recorded valid
      carry-forward from a current approved feature with an identical prior PNG manifest.
- [ ] No downstream delivery work continued while the design was `ReadyForReview`
      or rejected.

## 10. Negative criteria

The task must not:

- invent or remove screens, actions, paths, side effects, permissions, or states;
- use visual ambiguity to avoid specifying navigation or failure behavior;
- create hand-edited PNGs that cannot be regenerated from the renderer;
- depend on unpinned remote resources, CDN assets, live APIs, secrets, or production data;
- require separate untracked HTML/CSS/template source;
- treat successful rendering as visual approval;
- overwrite a rejected revision without retaining its decision trail; or
- authorize production frontend implementation without recorded human approval or a
  deterministic, auditable carry-forward of that authority under the unchanged-manifest rule.

## 11. Validation contract

Deterministic validation checks:

- renderer existence, `.mjs` extension, syntax, and documented Node invocation;
- use of generated SVG and pinned Sharp rather than an alternate canonical renderer;
- absence of network URLs and prohibited external fetches;
- approved design-guideline path/digest and declared token/deviation coverage;
- complete wireframe-to-manifest screen/state/viewport coverage;
- successful non-interactive execution and expected output set;
- valid PNG signature, expected dimensions, nonzero size, and nonblank pixel variance;
- stable output filenames and recorded SHA-256 digests;
- no unexpected generated files; and
- `design.md` provenance and approval fields.

The approval render runs in the repository's recorded canonical Node, Sharp, and
libvips environment. Within that environment, rerendering must produce identical
decoded pixels and the approved PNG SHA-256 hashes. Output from a different local
environment is only a preview; if it differs, approval uses a fresh canonical render
rather than accepting an unspecified visual tolerance.

Human review inspects images at original resolution for hierarchy, readability,
consistency, clipping, overflow, affordance clarity, state distinction, responsive
behavior, accessibility intent, and fidelity to the wireframe.

## 12. Completion evidence

| Evidence | Required content |
| --- | --- |
| Wireframe input | Path, validated digest, and combined reviewer/decision or optional earlier decision |
| Design guidelines | Path, approved SHA-256 digest, mapped rules, and approved deviations |
| Renderer | Path, SHA-256 digest, invocation, Node, Sharp, and libvips versions |
| PNG manifest | Screen ID, state, viewport, path, dimensions, SHA-256 digest |
| Render run | Timestamp, exact command, exit code, stdout/stderr artifact |
| Automated checks | Signature, dimensions, nonblank result, coverage, and reproducibility result |
| Visual review | Reviewer, guideline conformance, original-resolution inspection, findings, and disposition |
| Rejected revision | Renderer revision/commit, PNG hashes, reviewer, findings, and rationale; rejected PNGs are removed |
| Approval | Approver, timestamp, rationale, approved renderer digest and PNG manifest revision |

## 13. Deferral and complexity

Missing states or viewports are not silently omitted. A deferral records the affected
wireframe item, reason, consequence for implementation, owner, revisit condition,
and human approval. Frontend implementation remains blocked where the missing asset
affects approved scope.

- `low`: one simple screen and viewport with few states;
- `medium`: several screens/states, responsive variants, or established design-system
  composition; and
- `high`: several platforms, a new interaction language, complex visualization,
  accessibility risk, or a large state/viewport matrix.

High-complexity design is decomposed by coherent journey or platform. Each child may
own a renderer only when it has a disjoint declared screen manifest; there remains
one unambiguous renderer owner per PNG.

## 14. External issue hints

- Title: `Design: <feature title>`.
- Labels: `cis`, `task-type:visual-design`, and affected platform labels.
- Body: validated wireframe digest, required manifest, renderer/artifact paths,
  validation command, and human approval gate.
- Attachments: PNG previews may be attached, but canonical identity remains the
  repository artifact path and digest.
- Completion: exact screen pack approved in `design.md`; issue closure alone is not
  evidence of approval.

## 15. Open review questions

1. Should CIS provide the pinned Sharp runtime centrally, or should each target
   repository pin the supported version in its own package manifest?

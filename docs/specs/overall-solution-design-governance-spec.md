---
title: "Overall Solution Design Governance"
type: specification
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-03"
review_cadence: "on product-definition architecture workflow change"
cis:
  stable_id: change-impact-studio:spec:overall-solution-design-governance
---

# Overall solution design governance

## Purpose and position

The overall solution-design stage converts an Active, current technical intent into an
implementation-independent system design before feature decomposition. It defines how the
solution is divided, how those parts interact, where state and policy are owned, and which
boundaries the later UI-facing design must respect.

The governed sequence is:

```text
Active BRD -> Active technical intent
-> Active overall solution design + component sheet
-> Active high-level UI direction -> high-level backlog -> feature delivery
```

Technical intent remains the authority for technology and architecture direction. Its
`TI-MOD-*` and `TI-INT-*` entries are proposals until this stage projects them into the
reviewed topology. This stage may clarify responsibility and interaction detail but may not
silently add business scope or contradict an accepted technical decision.

## Atomic canonical bundle

`cis solution-design init` creates or reconciles two Markdown artifacts in the authority
repository:

- `architecture/overall-solution-design.md` describes architecture drivers, system context,
  topology, data ownership, integrations, security boundaries, deployment and recovery,
  verification, traceability, and the UI-design handoff.
- `references/component-sheet.md` provides the structured component catalogue: stable
  identity, name, classification, responsibility, owned concerns, exclusions, and BRD
  authority, followed by detailed responsibility profiles and the applicable `TI-INT-*`
  interaction catalogue.

The two files share the exact technical-intent digest, lifecycle status, reviewer, rationale,
approval time, and approved bundle digest. They form one review point. CIS never asks for an
independent approval per component or per file.

Component identity remains `TI-MOD-*` so traceability survives promotion from technical intent.
A component is a logical ownership boundary, not automatically a repository, process,
container, service, or independently deployed unit. Deployment decisions must be explicit.

## Generation and editing

Initialization requires an Active, valid, current technical intent with a structured product
module table. CIS deterministically projects relevant technical-intent sections and component
rows into managed blocks. Human design decisions and accepted exceptions outside those blocks
are retained across idempotent reruns.

An upstream technical-intent change refreshes the managed projection and resets bundle
approval. An unchanged rerun preserves both the approval and catalogue lifecycle. Stable-id or
path collisions fail closed.

## Validation and approval

Validation requires:

- both canonical files and managed markers;
- the complete required architecture and component-sheet sections;
- matching stable identities and source hashes;
- unique structured `TI-MOD-*` component rows referenced by the design;
- no unresolved placeholders;
- matching lifecycle and approval metadata; and
- an unchanged approved bundle digest for Active content.

`cis solution-design approve --reviewer <human> --reason <rationale>` approves the exact pair
atomically. Any material edit to either half makes the bundle stale. Agents may generate,
refine, and validate; they may not approve or manufacture human authority.

## Downstream gate and UI handoff

The solution-design module implements a workspace readiness check. Change creation and
high-level backlog generation remain blocked until the bundle is Active and current.

The high-level UI-direction stage consumes actors and journeys from the BRD plus capabilities,
permissions, contracts, state, and failure boundaries from this bundle. It owns shared product
character, application shells, navigation, reusable interaction patterns, visual language,
responsive behavior, and accessibility. Detailed screen inventories, actions, paths, and states
remain feature-wireframe work. Neither stage can change component ownership or trust boundaries
without first renewing the architecture bundle.

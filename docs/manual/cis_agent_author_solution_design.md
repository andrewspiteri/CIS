---
title: "cis agent author solution-design"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-08"
review_cadence: "on command or provider-policy change"
cis:
  stable_id: change-impact-studio:manual:cis-agent-author-solution-design
---

# `cis agent author solution-design`

Infer the existing product's overall architecture and component sheet from selected owned
implementation/test repositories, dictionary projections and current business/technical context.
The wizard exposes **Solution architecture and diagrams → Infer from existing repositories**.

```text
cis agent author solution-design --reference <owned-repo> [--reference <owned-repo>]...
  --provider <id> --actor <human> --repo <authority>
  [--transport <name>] [--timeout-seconds <30..86400>] [--approve-requests]
  [--format <human|json|agent>]
```

CIS refreshes discovered dictionaries and graphs, then freezes bounded, redacted implementation
snapshots and projections. It prepares a Review Required architecture bundle and sends the selected
evidence to the chosen provider in an isolated workspace. A canonical technical intent with stable
component identities is required. Upstream drafts may still be under review; ordinary initialization,
approval and activation gates are unchanged. Active architecture is protected.

The provider writes only `architecture/overall-solution-design.md` and `references/component-sheet.md`
under the documentation root. CIS checks the exact originals, evidence digests, lifecycle, stable
component IDs, required sections, human notes, implementation-area coverage and the scoped C4 model
before applying the pair. Rejection leaves the pair unchanged and retains the isolated candidate.
`cis agent resume` can repair the same run while its original bundle and context remain unchanged.

The narrative explains module responsibility, complete flows, state/consistency, integrations, trust,
operations and verification. Observed behavior, proposed changes and unresolved facts remain distinct.
Source citations stay in comments. Snapshots exclude manifests, deployment configuration, migrations,
dependencies, generated files and sensitive paths; unsupported deployment and recovery claims remain
unresolved. No repository code or build is executed by discovery.

CIS renders passive local SVGs and embeds them directly inside the overall design when applying the
bundle. Its schemaVersion 2 model follows C4: one system context, one container view and one or more
component views, each scoped to a single container. Context shows people and software systems;
containers identify applications and data stores; components explain the internals of selected
applications. Views label responsibilities, technology below context level, directed relationships
and uncertainty. They do not imply a deployment layout or turn logical ownership IDs into services.

Run `cis definition prepare --page architecture --workspace <authority>` to refresh the wizard's
companion diagram document. The wizard performs this automatically after inference. Repeated
preparation repairs missing images. `cis solution-design diagrams` repairs the embedded C4 display
in review-only drafts. Legacy four-view models remain readable; new inference requires C4.
Review both documents and their diagrams together.

Use `cis agent discover solution-design` to inspect the local disclosure envelope before choosing a
provider. This inference command never answers technical decisions, approves content or activates
the product baseline. `--approve-requests` only applies to requests within the provider's existing
execution permission ceiling.

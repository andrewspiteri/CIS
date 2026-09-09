---
title: "External Trackers Are Projections, Not Sources of Truth"
type: article
status: Draft
series: "Human and Agent Execution"
series_order: 8
owner: "Andrew Spiteri"
last_reviewed: "2026-09-09"
review_cadence: on tracker-synchronization change
summary: "How GitHub Issues and Jira can coordinate governed work without acquiring authority over scope, evidence, or completion."
cis:
  stable_id: change-impact-studio:article:external-trackers-are-projections
---

# External trackers are projections, not sources of truth

Teams need issues for assignment, prioritization, discussion, and visibility. They do
not need every engineering contract to become provider-specific ticket state.

CIS projects bounded work into external trackers while keeping the change dossier canonical.

## Preserve durable identity

Each projected task has a stable CIS work ID and durable external link. Provider issue
numbers are integration identities, not replacements for repository task identity.

## Use three-way reconciliation

Synchronization compares canonical task state, last synchronized state, and current
remote state. Independent changes on both sides become explicit conflicts rather than
last-writer-wins overwrites.

## Keep credentials outside documentation

Provider credentials come from environment configuration. Repository files contain
targets and mappings, never secret values.

## Remote state cannot approve work

Closing an issue, moving a board card, or receiving a webhook cannot grant plan,
verification, risk, or completion authority. CIS records the external state and requires
governed reconciliation.

## Providers remain replaceable

GitHub and Jira transports implement a provider-neutral contract in separate assemblies.
Adding or changing a provider does not redefine the task domain.

## Decide which fields may travel

Projection works best when the mapping is explicit. A tracker may receive title, bounded
description, task status, labels, dependencies, owner, and links back to canonical
documents. Sensitive context, internal evidence, source excerpts, approval rationale, or
fields the provider cannot preserve safely should remain in the repository.

The mapping also defines direction. Assignment may be coordinated remotely while
acceptance criteria remain repository-owned. A status update may flow both ways while
plan approval remains CIS-only. “Synchronize the issue” is too vague to govern these
differences.

## Three-way reconciliation protects both sides

Suppose the repository task moved from Ready to In Progress after the last sync while a
project manager moved the Jira issue to Blocked. Comparing only current local and remote
state cannot tell which value overwrote the other. The last synchronized snapshot shows
that both changed independently, so CIS creates a conflict.

The reviewer can preserve the remote coordination signal, update the canonical task when
authorized, or restore the remote projection. Last-writer-wins would discard information
and might let a timestamp decide engineering authority.

## Deletion and closure need special care

A deleted issue does not delete the canonical task. A closed issue does not prove that
validation ran or risk was accepted. CIS can record the remote condition, recreate the
projection when policy allows, or require conflict resolution.

Likewise, removing a canonical task should follow the change lifecycle rather than
silently deleting external history. Tombstones or durable links help prevent accidental
duplication when synchronization resumes.

## Keep synchronization idempotent

Retries are normal when remote services fail. Operations should carry stable idempotency
and link identities so a timeout does not create duplicate issues. Partial batch results,
rate limits, and provider errors remain visible, and successful items need not be
recreated on every retry.

Provider webhooks are untrusted external input. They can trigger reconciliation but
cannot directly mutate canonical authority without validation and the configured mapping.

## Credentials and content have separate boundaries

Provider tokens belong in environment or provider-native credential storage. Repository
profiles may name a target and field mapping without containing the credential itself.

Authorization to connect to a tracker also does not authorize publishing arbitrary
repository content. The projection must remain within its reviewed content mapping,
especially for private source, security findings, and agent evidence.

## Provider neutrality protects the task model

GitHub Issues and Jira express status, hierarchy, and workflow differently. Separate
transport adapters translate the stable CIS contract into provider capabilities. If a
provider cannot represent a dependency or state exactly, the limitation should be visible
rather than changing the canonical task to fit the tool.

This makes provider migration possible. Stable work IDs and canonical history survive
even if remote issue numbers, URLs, and board conventions change.

## Takeaway

Use trackers for coordination and visibility. Keep scope, dependencies, acceptance
criteria, evidence, and authority in repository-owned task records. Synchronize with
conflict detection rather than surrendering the source of truth.

## Canonical CIS sources

- [External tracker synchronization](../specs/external-tracker-synchronization-spec.md)
- [External tracker profile](../references/external-tracker-profile.md)
- [`cis tracker plan`](../manual/cis_tracker_plan.md)
- [`cis tracker resolve`](../manual/cis_tracker_resolve.md)

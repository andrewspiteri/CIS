---
title: "External Trackers Are Projections, Not Sources of Truth"
type: article
status: Draft
series: "Human and Agent Execution"
series_order: 8
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
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

## Takeaway

Use trackers for coordination and visibility. Keep scope, dependencies, acceptance
criteria, evidence, and authority in repository-owned task records. Synchronize with
conflict detection rather than surrendering the source of truth.

## Canonical CIS sources

- [External tracker synchronization](../specs/external-tracker-synchronization-spec.md)
- [External tracker profile](../references/external-tracker-profile.md)
- [`cis tracker plan`](../manual/cis_tracker_plan.md)
- [`cis tracker resolve`](../manual/cis_tracker_resolve.md)

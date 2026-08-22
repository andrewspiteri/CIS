---
title: "Task-Type Extension Provider Policy"
type: architecture-specification
status: Draft
version: "0.1"
scope: "Product:ChangeImpactStudio"
owner: "Andrew Spiteri"
last_reviewed: "2026-08-13"
review_cadence: "on task-provider contract change"
cis:
  stable_id: change-impact-studio:spec:task-type-extension-policy
---

# Task-Type Extension Provider Policy

## Purpose

Define deterministic registration, conflict, replacement, selection, upgrade, and
removal behavior for task types supplied by loaded CIS modules. Extensions add
capability-specific work without weakening or silently replacing the core delivery
contract.

## Identity and reserved namespaces

- Provider keys and task-type keys are lowercase namespaced stable identities.
- `cis.core` owns `core.*`; extension providers may never register a `core.*` key.
- Extension keys use a provider-controlled namespace such as
  `parr.search.projection`.
- One stable key identifies one semantic responsibility across versions. Renaming a
  key creates a new type and requires an explicit migration rule.
- Task instances always preserve provider key, type key, and definition version.

## Registration

Providers are ordered by provider key only to produce deterministic diagnostics and
display. Registration order never grants precedence. Startup fails before planning
when any of these conditions exists:

- duplicate provider key;
- duplicate task-type key, including different case;
- extension use of the reserved `core.*` namespace;
- missing dependency key;
- self-dependency or dependency cycle; or
- malformed key, version, creation policy, gate, or complexity contract.

The current host's duplicate-key rejection is therefore normative. Last-loaded-wins
behavior is prohibited.

## Conflict and capability selection

Different keys may still provide mutually exclusive implementations of one
capability. A provider must declare a stable capability key and any incompatible
task-type keys. Planning behaves as follows:

1. If only one applicable provider exists, select it and record activation evidence.
2. If applicable providers are additive, instantiate each with their declared
   dependencies.
3. If applicable providers conflict and the repository has no approved selection,
   fail planning with every candidate and its evidence; do not choose by order.
4. A repository selection lives in canonical repository delivery/architecture policy
   and records capability, selected provider/type, rejected alternatives, rationale,
   approver, and effective version.
5. A missing selected provider is a planning error, not permission to fall back.

## Replacement policy

Core types are non-replaceable. An extension may satisfy additional product-specific
work but cannot remove Coordination, Documentation, Verification, Independent
Assurance, Final Delivery Sweep, or any triggered core type.

For extension types, replacement is allowed only through an explicit repository
selection that names the replaced and replacement keys. The replacement must:

- preserve or strengthen the common task-type contract;
- map every predecessor, consumer, requirement, accepted impact, gate, acceptance,
  negative criterion, validation obligation, and existing task instance;
- preserve historical Markdown, approvals, evidence, deferrals, external links, and
  lifecycle events;
- declare a rollback path while both provider versions are available; and
- pass plan validation before becoming effective.

Semantic overlap without that record is a conflict. An extension cannot declare
itself a replacement unilaterally.

## Version upgrades and removal

A compatible definition upgrade retains the stable key and increments its version.
Re-import may refresh deterministic sections only; it preserves human-managed state.
Breaking semantic changes require a new key or a reviewed instance migration.

Removing a provider never deletes task instances. Existing tasks become
`provider-unavailable`, remain canonical, and block regeneration or completion until
the provider is restored, the task is completed under its recorded version, or a
human-approved replacement migration is applied.

## Evidence and diagnostics

The plan records loaded provider versions, all applicable candidates, trigger
evidence, selection/replacement policy IDs, and rejected alternatives. Human, JSON,
and agent output must expose conflicts without truncating candidate identity or the
required next decision.

## Implemented command and persistence boundary

CIS implements deterministic provider/key/dependency validation, capability and
conflict declarations, canonical repository selections, unresolved-conflict planning
failures, explicit compatible replacements, and evidence-preserving instance
migration. The canonical selection ledger is
`<root>/references/task-type-capability-selections.md`.

Use `cis plan capability status|select` for repository selection and
`cis plan task migrate-type` for each existing instance. Selection never silently
migrates tasks. Migration preserves stable identity/path, evidence, deferrals,
external links, and history while adding the new type's obligations and returning
the task/plan to Draft.

When import encounters an unresolved applicable conflict, it appends one
content-fingerprinted `task-capability-conflict-detected` event to the change dossier.
Repeated detection is idempotent. Later selection or migration does not erase the
historical candidates, reason, or fingerprint.

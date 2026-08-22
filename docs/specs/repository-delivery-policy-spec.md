---
title: "Repository Delivery Policy Specification"
type: governance-specification
status: Draft
version: "0.1"
scope: "Product:ChangeImpactStudio"
owner: "Andrew Spiteri"
last_reviewed: "2026-08-13"
review_cadence: "on repository delivery workflow change"
cis:
  stable_id: change-impact-studio:spec:repository-delivery-policy
---

# Repository Delivery Policy Specification

## Purpose

Define the canonical repository-owned policy that governs branches, commits, pull
requests, pushes, protected targets, and external delivery actions. CIS task approval
authorizes bounded implementation; it does not by itself authorize Git history or
remote-state mutation.

## Canonical target document

`cis repo init` seeds `<root>/specs/repository-delivery-policy-spec.md`. Each target
repository maintainer reviews and completes it. Multi-repository work applies each
target's own policy; the authority documentation repository must not overwrite
participant policy.

The document records:

- default/base branch and protected branches;
- branch naming, creation point, reuse, and worktree rules;
- whether direct work on the current branch is permitted;
- commit scope, message/signing, authorship, co-author, and commit-per-task rules;
- required local checks before commit and push;
- push remote, branch, force-push, and lease policy;
- pull-request requirement, target, draft/review state, templates, labels, reviewers,
  checks, and merge method;
- issue/change/task linkage and external tracker policy;
- who may create branches, commit, push, open/update/merge/close pull requests, and
  delete branches;
- secret, generated artifact, and evidence handling; and
- repository-specific exceptions with approver and expiry/review condition.

## Safe default when unresolved

Until the seeded policy is explicitly reviewed, CIS uses `local-only`:

- agents may inspect and make approved in-scope working-tree changes;
- no branch switch/creation, commit, push, pull request, merge, tag, release, force
  update, or remote issue mutation is inferred;
- existing user changes and branch state are preserved;
- a requested remote/history action stops for explicit user authorization; and
- inability to publish does not invalidate local implementation evidence, but final
  handoff records the outstanding delivery action.

This default is deliberately conservative and applies even when repository hosting
or credentials are detectable.

## Precedence and authority

Applicable platform protection rules and organization policy take precedence over
the repository document. Within the repository, an explicitly approved delivery
policy takes precedence over generated hints. User authorization for a specific
action is still required where the operating agent's safety model requires it.

An external issue or pull request cannot approve a CIS plan, wireframe, design,
deferral, residual risk, or final feature acceptance unless the canonical CIS record
contains the corresponding human decision.

## Multi-repository changes

Each repository produces its own branch/commit/push/PR plan and validation evidence.
The Coordination task records cross-repository ordering and compatibility, while
Final Delivery Sweep records actual revisions and outstanding handoffs. One
repository's permissive policy never broadens another repository's authority.

## Validation

Repository doctor and plan validation should eventually report:

- missing or Draft delivery policy when remote/history work is planned;
- unknown base/remote/target branch;
- requested action prohibited by policy;
- missing required checks, linkages, reviewers, or artifact handling; and
- policy conflicts across targets that require coordination.

Validation is read-only. It must not create branches, commits, pushes, or pull
requests to determine whether they would succeed.


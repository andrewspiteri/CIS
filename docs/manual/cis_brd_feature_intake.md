---
title: "cis brd feature intake"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-15"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-brd-feature-intake
---

# `cis brd feature intake`

Introduce a prepared feature BRD into an existing product and create or connect its
implementation repository. The VS Code **CIS: Add Feature from BRD** command provides
the form, file pickers, integration selection, preview and apply steps.

The extension continues into a resumable **Feature definition wizard** after creation.
It covers foundation, business, technical direction, architecture, integrations and
dictionaries, experience, delivery, and final review. Open **CIS: Open Feature Definition
Wizard** to resume it. Saved requests are listed on the foundation page.
The [status](cis_brd_feature_wizard_status.md) and [save](cis_brd_feature_wizard_save.md)
commands own its review state and preserve answers in the canonical request.

```text
cis brd feature intake --input <request.json> --workspace <authority>
  [--dry-run | --yes --expected-plan <preview-planHash>]
  [--format <human|json|agent>]
```

The JSON request contains:

```json
{
  "title": "Referrals",
  "slug": "referrals",
  "sourcePath": "C:/requirements/referral-brd.md",
  "repositoryMode": "new",
  "repositoryPath": "C:/projects/referrals",
  "documentationRoot": "docs/cis",
  "integrationRepositories": ["backend", "frontend"],
  "actor": "Reviewer name"
}
```

Use `new` for a folder that does not exist, or `existing` to connect a repository
already on disk. Integration IDs must identify registered participant repositories.
An external dependency remains context owned by another authority.

Preview validates the source, paths, repository ownership and catalog collisions.
It detects numbered open questions or decisions without answering them. It creates
no repository, request or retained-source files. Applying requires the exact preview
hash; source, registry, repository-existence or product-baseline changes require a
fresh preview. A local Git executable is needed when creating a new repository.

Applying initializes the new local Git repository when requested, imports it into
the product as an owned participant, and retains the UTF-8 Markdown BRD byte-for-byte
under `.cis/inputs/features/<slug>/source.md`. The canonical Draft request is at
`<authority-docs-root>/specs/feature-requests/<slug>/request.md`. It records the source
hash, selected repository, integrations, actor, current product-definition hash and
detected open decisions. The source is durable input, outside disposable local caches.
The extension then rebuilds the graph and refreshes the workspace once.

Draft intake requests and their retained sources are feature review material, not
automatic product BRD candidates. A repository created by intake stays outside the
current BRD and technical-intent baselines while it contains only its unchanged CIS
scaffold and has not already entered the BRD baseline. It remains registered and
available to the feature wizard. New code, added or changed documentation, missing
scaffold evidence, or adoption into the product baseline restore ordinary repository
checks. This also applies to earlier intakes without rewriting their product approvals.

An identical retry is unchanged. Existing requests and sources are never replaced.
A partial setup reports its paths and preserves files for recovery. Folder and source
links are rejected when they cross filesystem reparse points.

This facility also works with an empty or `no-planned-work` backlog. It records proposed
scope without changing existing approved documents or approving requirements. Scope
review and reconciliation into the governed product backlog remain necessary before
[`cis brd backlog start`](cis_brd_backlog_start.md) can create a feature specification.
Intake does not scaffold application code, select a stack, create a remote or push Git.

Exit `0` means previewed, created or unchanged; `3` requests confirmation; `5` reports
invalid input, stale preview, collision or incomplete setup.

---
title: "cis brd feature wizard navigation"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-19"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-brd-feature-wizard-navigation
---

# `cis brd feature wizard navigation`

```text
cis brd feature wizard navigation --workspace <authority> [--format human|json|agent]
```

Returns the product identity, registered repositories and saved high-level feature
definitions, including page progress, the next unresolved page and proposed repository
work. The VS Code Product, High-level features and Delivery changes views share this
projection. Opening a feature uses its stable slug and restores its saved page and drafts.

This read-only projection shares one product-baseline evaluation across features and is
included in the checked workspace startup snapshot. It does not start providers, rebuild
graphs, approve work or scan every change's plan. A feature with unreadable source or
review evidence reports its own findings without hiding other readable features.

Each repository feature belongs to one high-level feature and one product-owned
participant. Multiple work items may target a repository. Dependencies refer to sibling
work identifiers; independent items can be planned in parallel. Existing authority change
dossiers may be explicitly linked to work items and retain their own lifecycle. Unlinked
changes remain visible under Other product changes. No relationship is inferred from titles.

Use [wizard save](cis_brd_feature_wizard_save.md) to edit the delivery breakdown. These
entries are proposed scope, not approved backlog items or executable plan tasks.

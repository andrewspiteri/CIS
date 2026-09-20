---
title: "cis solution-design diagrams"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-solution-design-diagrams
---

# `cis solution-design diagrams`

```text
cis solution-design diagrams [--workspace <path>] [--model <json-path>]
  [--format <human|json|agent>]
```

Render a review-only architecture draft's C4 model into passive local SVGs and embed the images in
`architecture/overall-solution-design.md`. Missing SVGs are repaired. Supplying `--model` replaces
the hidden model with reviewed schemaVersion 2 JSON without re-authoring the narrative or component
sheet. Ordinary inference generates and embeds these diagrams automatically.

The model contains `schemaVersion: 2` and `views`. Each view has `id`, `title`, `notes`, `level`,
`scopeId`, `nodes` and `edges`. Exactly one `context` view shows the product software system, people
and external software systems. Exactly one `container` view shows applications and data stores
inside that product. At least one `component` view zooms into a single container from the container
view. There can be 3–10 views, each with 2–16 connected nodes and 1–24 directed relationships.

Nodes have `id`, `label`, `kind`, `description`, `layer` and `status`. The kinds are `person`,
`software-system`, `container` and `component`. Containers/components also require `technology`
and `parentId`, identifying their owning system/container. Other types omit these fields. Reused
identities must have the same metadata across views. Place internal nodes in layers 1/2 and
supporting context in layers 0/3. Edges have `from`, `to`, `label`, `status` and, below context level,
`technology`. Evidence status is `observed`, `proposed` or `unresolved`. Use meaningful relationship
verbs, responsibilities and explicit evidence limitations. The authoring workflow supplies the
complete bounded schema to its provider.

These levels follow the [C4 model](https://c4model.com/diagrams). Containers are software boundaries,
not deployment instances. Deployment and code-level views are not required for this overall design.

The command preserves frontmatter, human notes, source citations, component identities and upstream
documents. Active bundles and drafts whose technical-intent baseline changed are protected.
Modified SVGs and modified generated display blocks are preserved and reported for reconciliation.
It neither infers evidence from the JSON nor approves the architecture. Use
`cis agent author solution-design` to infer a model from implementation. Legacy diagram models remain
readable, but new inference and this rendering command require C4.

Run `cis definition prepare --page architecture` afterwards to refresh the companion diagram
document and wizard projection.

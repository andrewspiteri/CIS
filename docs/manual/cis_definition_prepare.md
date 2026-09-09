---
title: "cis definition prepare"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-04"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-definition-prepare
---

# `cis definition prepare`

```text
cis definition prepare --page <foundation|business|technical|architecture|contracts|experience|delivery|review> [--workspace <path>] [--format <human|json|agent>]
```

Prepares or refreshes one page from the exact current upstream draft. Technical and experience
pages initialize their questionnaires and generate their governed documents after every decision
is resolved. Architecture generates the solution-design bundle and high-level diagrams. Delivery
builds the high-level backlog. The command never records human approval.

For an existing system, use **Architecture → Infer from existing repositories** or
`cis agent author solution-design` first. Architecture preparation preserves the inferred narrative
and renders four SVG views from its diagram model. It repairs missing images and preserves modified
assets for reconciliation. Upstream review and activation gates remain unchanged.

Preparing `business`, `technical` or `contracts` discovers draft inventories in the product-owned
application repositories and consolidates their implementation facts into the authority's
`references/` dictionaries. A Repository column attributes each consolidated row and scopes
otherwise identical contract identities. CIS then refreshes their graphs and wizard artifacts.
Dictionary preparation does not edit or infer the BRD.
The ERD includes native SVG diagrams generated from its relationship table, grouped by
repository, owner and declaring entity. Open `references/erd.md` in Markdown preview and
expand an entity under **Relationship diagrams**, or follow its full-size SVG link.
Declared multiplicities and unresolved targets are shown without inferring database keys
or required participation. Preparing again refreshes the managed diagram block in Draft
ERDs and repairs missing images; human table rows, prose and reviewed ERDs are preserved.
The business page exposes **Prepare existing-system context** with discovered row counts and
links to each dictionary. Counts describe the last preparation, not a live completeness claim.
Dependency repositories are excluded.

Preparation covers API contracts, persistent fields and relationships, workflow states,
permissions, screens/routes, commands, events, projection declarations, exceptions, code guards,
configuration keys, packages, module ownership and traceability. The built-in scanner
recognizes literal NestJS controller routes and declared request/response/guard metadata,
TypeORM decorated entity fields and relationships, TypeScript status/state enums, NestJS queue
processors and event handlers, literal event emissions, exception throws and immediate rejection
guards. Configuration discovery records environment keys without their runtime values.
Observed request handlers and rejection guards remain Draft observations, not approved command
semantics or business policies. TODO placeholder rows count as zero dictionary entries.
Computed or inherited declarations, global routing prefixes, effective authorization,
workflow transitions and business intent still require review. Zero extracted rows do not
prove that a capability is absent.

CIS refreshes untouched managed Draft starters, including obsolete extracted rows, and keeps
content-addressed recovery copies under `.cis/local/reference-preparation/backups/`.
For a human-edited Draft with matching table headers, it only appends missing identities.
Reviewed dictionaries, existing human rows and lifecycle metadata are preserved and reported
for manual reconciliation. No approval is recorded. Preparation is explicit work; status
commands read the last report without rescanning application source.

In VS Code, **Infer from existing project** automatically performs this preparation after
repository/provider selection and evidence-disclosure confirmation. Authoring supplements
the fresh dictionary projection with cached implementation/test snapshots and requires
hidden coverage evidence for every indexed source area. Use `cis agent discover brd` to
preview the exact source payload locally before authorizing a provider. Use **Draft
from references** for selected document evidence. Direct `cis agent discover` and `cis agent author`
BRD/technical-intent commands also prepare dictionaries and graphs before freezing evidence when
owned repository references are selected. Both authoring paths produce a Review Required
draft. The later contracts page reviews and extends the inventories established here.

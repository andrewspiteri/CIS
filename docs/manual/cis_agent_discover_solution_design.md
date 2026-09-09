---
title: "cis agent discover solution-design"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-08"
review_cadence: "on discovery-policy change"
cis:
  stable_id: change-impact-studio:manual:cis-agent-discover-solution-design
---

# `cis agent discover solution-design`

Prepare and inspect local architecture evidence without contacting an agent provider.

```text
cis agent discover solution-design --reference <owned-repo> [--reference <owned-repo>]...
  --actor <human> --repo <authority> [--format <human|json|agent>]
```

This explicit preparation refreshes discovered dictionaries and graphs, builds reusable bounded
implementation/test snapshots for selected owned repositories, and prepares the Review Required
overall design and component sheet from the current technical intent. It does not edit the BRD,
technical intent or questionnaire, and does not approve either architecture document.

The disclosure envelope is `.cis/local/agents/implementation/solution-design-selection.json`.
Inspect its context paths, source IDs, digests, required coverage areas and omissions. Snapshots use
the same redaction and exclusions as BRD and technical-intent discovery. Dependency implementation
is excluded. The scaffold is a starting projection, not an inferred or approved architecture.

Then select a provider through `cis agent author solution-design` or the wizard's Architecture
page. Authoring performs the same preparation automatically. Ordinary `definition status` remains
a read of existing state and does not run discovery.

---
title: "cis brd layout"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-10-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-brd-layout
---

# `cis brd layout`

Map an imported BRD's existing headings to CIS section roles without renaming headings
or rewriting business content. Use this when requirements are distributed across domain
or lifecycle sections. Preview the selections and extracted identities before applying.

```text
cis brd layout --workspace <authority> --input <layout.json> --format json
cis brd layout --workspace <authority> --input <layout.json> --yes --review-hash <preview-reviewHash>
```

The input is UTF-8 JSON, limited to 32 KiB. The reconciled BRD must fit within 512 KiB.
Headings match exactly, including numbering but excluding the Markdown `#` prefix.
Each selected heading must occur once outside frontmatter, comments and fenced examples.

This synthetic example selects two requirement sections:

```json
{
  "schemaVersion": 1,
  "sections": {
    "Functional requirements": [
      { "heading": "4 Capture requirements", "includeChildren": true },
      { "heading": "9 Replay requirements", "includeChildren": false }
    ]
  }
}
```

`includeChildren` defaults to true and includes nested sections. False selects only the
body before the next heading of any level, which can keep a following acceptance section
out of the requirement selection. Use `$introduction` to select prose between frontmatter
and the first heading. It does not skip a title heading. Selections within one role must
not overlap; selected ranges are read in source order. A section may serve multiple roles.
Do not map a parent section containing acceptance cases or decision tables as functional
requirements unless those entries really are requirements. CIS does not infer business
roles from identifier prefixes.

If a domain contains separate `9.1 Replay requirements` and `9.2 Replay acceptance`
subheadings, select `9.1 Replay requirements` directly and omit the acceptance heading.
Select any additional requirement subheadings separately; do not also select their parent
with `includeChildren: true`, because those ranges would overlap.

The supported roles are Executive summary, Business outcomes, Scope, Stakeholders and
actors, Business capabilities and processes, Functional requirements, Quality, regulatory,
and operational requirements, Constraints and assumptions, Success measures, and Traceability.
Unmapped roles retain the existing canonical-heading and alias behavior. Missing roles
are reported; mapping is navigation, not proof of semantic completeness.

Open questions is deliberately not a mapping role. Keep that canonical heading and its
supported question/answer workflow. A future activation-decision register must not be
mapped to Open questions to imply that its decisions are answered, nor must every later
activation choice be converted into a question blocking today's baseline approval.

The command returns selected roles, readable functional requirement IDs, missing section
roles and a `reviewHash` binding the exact document and mapping. A changed document or
mapping requires a new preview. Applying stores the map in the document's `cis_brd_layout`
frontmatter field, saves a local backup and clears its approval metadata to Review Required.
Business narrative and managed evidence blocks remain unchanged. The map is included in
the approval content digest; renamed, missing or ambiguous headings fail validation.

Applying a map does not assess sources, answer questions or approve the BRD. Rebuild the
workspace graph, reconcile the document and run `cis brd validate` after applying. Validation
and backlog generation use the same mapped requirement reader. Narrative forms such as
`**CAPTURE-01 Capture observations.** Preserve original events.` are supported alongside
the existing dash-separated form and requirement tables. Continuation paragraphs and
constraint tables stay attached to the preceding requirement until a heading or another
requirement starts.

```text
cis graph build --workspace <authority>
cis brd reconcile --workspace <authority>
cis graph build --workspace <authority>
cis brd validate --workspace <authority>
```

The second graph build refreshes any catalog or managed-evidence changes from reconciliation.

Exit code 0 means preview, applied or unchanged; 2 means an invalid input, stale review hash
or I/O failure. No model or external service is called.

## Related commands

- [`cis definition status`](cis_definition_status.md) describes importing and selecting documents.
- [`cis brd validate`](cis_brd_validate.md) checks approval readiness.
- [`cis brd sections`](cis_brd_sections.md) proposes content additions when content is actually missing.

---
title: "cis brd validate"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-brd-validate
---

# `cis brd validate`

Validates canonical BRD structure, source assessment, participant evidence, and
approval readiness without changing files.

## Synopsis

```text
cis brd validate [--workspace <path>] [--format <human|json|agent>]
```

Validation requires the managed identity and blocks, all required sections without
TODO/TBD placeholders, current hashes for every discovered candidate, one allowed
assessment and rationale per candidate, readable graphs, and fresh participant graphs.

Imported documents can use numbered headings, different heading levels, and common
equivalents such as Business objectives, Success criteria, or Assumptions and constraints.
Validation recognizes those headings without rewriting the source. Related topics such
as Out of scope do not satisfy Scope, and headings inside code examples are ignored.
Recognized sections still require substantive content and human review.

For other layouts, use [`cis brd layout`](cis_brd_layout.md) to preview and apply explicit
source-heading mappings. The map preserves the narrative, is covered by the approval digest,
and fails validation when a mapped heading disappears or becomes ambiguous. It does not
convert future activation decisions into answered open questions.

Selected imported BRDs also pass the same requirement reader used by backlog generation.
It accepts requirement tables and bold requirement paragraphs, with optional bullets,
numbered sections and original domain IDs such as `MD-01` or `EX-01`. Paragraph
continuations, constraint lists and constraint tables remain attached to their requirement.
Bold titles can use `**MD-01 Capture events.**`
or the existing `**MD-01 — Capture events.**` form. Code examples
and comments are excluded. Missing requirement text, duplicate IDs or a functional
section without readable identified requirements block approval with a format finding.
This normalization creates an internal read model; it does not rewrite or approve the
imported document.

Participant baseline differences are reported as drift warnings. They do not prevent
an explicit approval because approval captures the then-current participant builds.
After approval, the same difference makes the effective state `Stale`.

## Exit codes

| Code | Meaning |
| ---: | --- |
| `0` | BRD content and evidence are valid. |
| `2` | Workspace or canonical configuration is invalid. |
| `4` | Canonical BRD or required graph evidence is missing. |
| `5` | BRD validation found blocking gaps. |

## Related commands

- [`cis brd status`](cis_brd_status.md)
- [`cis brd approve`](cis_brd_approve.md)
- [`cis brd init`](cis_brd_init.md)

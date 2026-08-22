---
title: "cis brd feature validate"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-19"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-brd-feature-validate
---

# `cis brd feature validate`

```text
cis brd feature validate --item <HLT-ID>
  [--workspace <path>] [--format <human|json|agent>]
```

Validates the authority-owned feature specification linked from one high-level backlog
item. Validation covers stable identity, BRD and backlog traceability, repository targets,
all required sections, structured `FEAT-*` rows, allowed implementation surfaces,
public/customer/backoffice UI coverage, placeholders, source-item currency, and approval
evidence.

A complete Draft reports `Ready for Approval` only while its high-level backlog remains
Active/current. Upstream authority drift reports `Review Required`. An approved unchanged specification
reports `Active`; an approved specification whose content or backlog item changed reports
`Stale`. Exit `0` means the validation ran and passed, `4` means the item or feature file
is missing, and `5` means the document is structurally incomplete.

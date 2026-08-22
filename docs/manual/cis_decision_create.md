---
title: "cis decision create"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-decision-create
---

# `cis decision create`

Records a change-local question, candidate options, evidence, and approval gate.

```text
cis decision create <change-id> --question <text> --category <category>
  --option <text> --option <text> --evidence <text>
  [--advisory] [--required-before <gate>]
  [--repo <path>] [--format <human|json|agent>]
```

Categories are architecture, scope, contract, data, security, operations, delivery,
and implementation. Decisions block plan approval by default; `--advisory` makes the
question non-blocking. At least two distinct options and evidence are required. An
identical question returns `unchanged` rather than creating a duplicate.

---
title: "cis design wireframe-validate"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-20"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-design-wireframe-validate
---

# `cis design wireframe-validate`

Validates textual screen behavior before asking a human to approve it.

```text
cis design wireframe-validate <change-id> [--repo <path>] [--format <human|json|agent>]
```

The command is non-mutating and may be run while `wireframes.md` is Draft,
InProgress, or ReadyForReview. It checks:

- canonical document type and matching change identity;
- case-sensitive standalone `TODO`/`TBD` authoring markers and the phrase
  `to be completed` without confusing product names such as Friends Todo;
- a non-empty, unique screen inventory;
- exactly `public`, `customer`, or `backoffice` classification per screen;
- concrete application routes beginning with `/`;
- one matching definition with Description, Actions and paths, and States for every
  screen;
- well-formed, unique action or automatic-transition IDs with destination paths and
  destination screens; and
- journey/requirement coverage plus the human approval section.

Successful agent output includes screen/action counts and the approval-body digest.
`wireframe-approve` reruns the same validator before recording human authority.

Exit `0` means the behavioral contract is mechanically ready for review. Exit `2`
means approval must not be requested until every reported error is resolved.

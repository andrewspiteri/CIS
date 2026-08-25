---
title: "cis design wireframe-approve"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-23"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-design-wireframe-approve
---

# `cis design wireframe-approve`

Records explicit human approval of textual screen behavior and navigation paths.

```text
cis design wireframe-approve <change-id> --reviewer <identity> --reason <rationale> [--repo <path>] [--format <human|json|agent>]
```

Approval reruns `cis design wireframe-validate` and is blocked by any structural,
classification, action/path, state, coverage, or placeholder error. Authoring markers
are case-sensitive standalone `TODO`/`TBD` tokens, so product copy such as Friends Todo
does not produce a false positive. CIS records a digest that excludes the approval
section so the decision itself does not change the approved content hash.

This optional command supports teams that want an early behavior-only checkpoint. The
default streamlined workflow validates the wireframe, renders the design, and uses
`cis design approve` to approve the exact wireframe digest and visual pack together.

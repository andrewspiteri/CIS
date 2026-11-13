---
title: "cis plan approve"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-23"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-plan-approve
---

# `cis plan approve`

Records explicit human approval for a valid plan when its existing planning path
needs new exact-plan authority.

```text
cis plan approve <change-id> --reviewer <identity> --reason <rationale> [--repo <path>] [--format <human|json|agent>]
```

Approval reruns all plan validation and blocks on incomplete impact, open decisions,
missing coverage, invalid dependencies, or absent acceptance and validation. A second
approval is `unchanged`. AI and deterministic analysis cannot invoke approval
implicitly. Approval records the reviewer, rationale, and event evidence.

Current outcome authority already covers its derived tasks, supporting work and
authorized decomposition. Do not ask for this approval again because a task was
added, split or reordered, or a generated plan hash changed. Preserve the original
human decision and apply normal validation and admission checks.

This command is the explicit fallback for legacy plans built or imported without reusable
outcome or feature authority, and for cases where `cis plan derive` reports an exceptional decision.
When `cis plan derive` succeeds, the current feature approval is already recorded as the
plan's approval basis and another plan-approval stop is neither required nor permitted.
An actual scope, contract or overall-limit expansion requires its specific new decision,
not repeated approval of unchanged work.

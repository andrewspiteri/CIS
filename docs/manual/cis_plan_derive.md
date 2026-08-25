---
title: "cis plan derive"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-23"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-plan-derive
---

# `cis plan derive`

Carries the authority of a current, explicitly approved feature specification through
deterministic impact adoption and exact plan generation without asking for duplicate
impact and plan approvals.

```text
cis plan derive <change-id> --file <repo-relative-spec.md> [--repo <path>] [--format <human|json|agent>]
```

The command requires exactly one registered approval authority to confirm that the
specified feature is Active, current, and linked to the supplied path and digest. CIS
uses the original human reviewer and rationale as provenance; the command does not
create or infer new human authority.

Derivation is atomic. It accepts eligible deterministic findings, imports the feature,
validates the complete task pack, and records plan approval against the approved feature
digest. If any step fails, impact dispositions and generated plan files are restored to
their previous state.

When the generated change proposal still contains only its initialization placeholder,
derivation copies the approved feature requirement IDs and acceptance criteria into the
proposal's outcome-level acceptance section. Existing human-managed acceptance criteria
are preserved. This is exact authority carry-forward, not newly inferred scope.

CIS stops instead of carrying authority forward when analysis is truncated, a finding
is deferred or low-confidence, the feature approval is stale or ambiguous, an extension
capability is unresolved, a blocking decision remains open, scope does not match the
approved feature, or the generated plan is invalid. Resolve only the reported exception,
then rerun the command. Use `cis impact accept|reject|defer`, `cis plan import-spec`, and
`cis plan approve` as the explicit fallback path when carry-forward is not applicable.

Exit `0` means the exact generated plan is valid and Approved. Exit `5` means a human
decision or another gate is required. Exit `2` means the request or source is invalid.

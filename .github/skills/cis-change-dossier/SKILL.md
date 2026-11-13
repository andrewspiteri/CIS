---
name: cis-change-dossier
description: Create, inspect, and manage CIS repository-owned change dossiers against an exact Git or graph baseline. Use when beginning a reviewed engineering change, checking its lifecycle, or closing it after verified completion.
---

# CIS Change Dossier

1. Read existing authority for the outcome, scope, constraints, overall limits and delegation. Confirm exact graph roots; keep necessary supporting work in the same outcome.
2. Run `cis change create --title <title> --outcome <outcome> --root <id>#<kind> --format agent`.
3. Use `cis change list`, `cis change show <id>`, and `cis change status <id>` to recover durable state.
4. If genuine source or governance input changed before any impact finding or generated task exists, run `cis change rebaseline <id> --actor <identity> --reason <rationale>`; inspect the appended event. Managed dossier edits alone never require rebaseline.
5. Preparing the draft requires no preliminary human approval. If applicable authority is missing, prepare the complete scope and finite limits, then record the actual human decision with `cis change authorize <id> --input <request.json> --reviewer <human> --reason <reason>`. Reuse that authority for derived impact, plan and task records; retain the original reviewer and decision reference.
6. Keep one shared outcome description. Tasks add specific work and acceptance details and reference shared constraints and limits. Generate only applicable records; preserve historical full dossiers and exact legacy approvals within their original scope.
7. Run `cis change close <id>` only after the user explicitly authorizes closure and completion evidence exists.

Do not invent an outcome, create duplicate dossiers, rebaseline reviewed impact or task evidence, change baselines silently, hand-edit managed tables, or treat closure as verification.

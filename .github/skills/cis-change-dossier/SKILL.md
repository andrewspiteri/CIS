---
name: cis-change-dossier
description: Create, inspect, and manage CIS repository-owned change dossiers against an exact Git or graph baseline. Use when beginning a reviewed engineering change, checking its lifecycle, or closing it after verified completion.
---

# CIS Change Dossier

1. Confirm the outcome and exact graph roots.
2. Run `cis change create --title <title> --outcome <outcome> --root <id>#<kind> --format agent`.
3. Use `cis change list`, `cis change show <id>`, and `cis change status <id>` to recover durable state.
4. If genuine source or governance input changed before any impact finding or generated task exists, run `cis change rebaseline <id> --actor <identity> --reason <rationale>`; inspect the appended event. Managed dossier edits alone never require rebaseline.
5. Keep proposal outcome criteria human-reviewable before plan approval.
6. Run `cis change close <id>` only after the user explicitly authorizes closure and completion evidence exists.

Do not invent an outcome, create duplicate dossiers, rebaseline reviewed impact or task evidence, change baselines silently, hand-edit managed tables, or treat closure as verification.

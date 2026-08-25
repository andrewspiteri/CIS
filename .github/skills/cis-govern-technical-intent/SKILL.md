---
name: cis-govern-technical-intent
description: Initialize, draft, validate, review, approve, or recheck the canonical workspace technical intent derived from an Active BRD and exact participant graph baselines. Use after BRD approval and before change-dossier creation or bounded planning.
---

# Govern Technical Intent

## Workflow

1. Confirm `cis brd status --workspace <workspace>` reports Active, valid, and current.
2. Build and strictly validate the workspace graph.
3. Run `cis technical-intent init --workspace <workspace> --format agent` to bind the canonical authority document to the active BRD hash and participant graph builds.
4. Complete architecture boundaries, data and consistency, contracts, security/privacy, operations/recovery, quality evidence, and delivery constraints without broadening the BRD.
5. Record each bounded unresolved choice in `Open technical decisions` with a stable `TI-DEC-*` ID, required gate, status, and rationale. Agents may propose options but never select or defer them without explicit human authority.
6. Run `cis technical-intent validate` and `status`. Resolve every placeholder, unresolved decision, stale BRD hash, and participant-baseline drift.
7. Run `cis technical-intent approve --reviewer <human> --reason <rationale>` only after the user explicitly authorizes that exact approval.
8. After approved implementation changes participant graph IDs, run `cis technical-intent refresh`. Never request renewed BRD, technical-intent, or backlog approval when all three remain Active/current; review only the stage that blocks on a material semantic change.
9. Rebuild and strictly validate the workspace graph after approval. Recheck status before `cis change create` and `cis plan build|import-spec|derive`.

## Guardrails

Preserve managed baseline markers and identities. Do not treat Draft, Ready for Approval, or Stale as Active; infer architecture decisions; approve on a user's behalf; edit approval hashes; or bypass the readiness gate. Pure managed-baseline drift is reconciled by `cis technical-intent refresh`; material business or technical drift requires renewed human review.

---
name: cis-govern-technical-intent
description: Capture high-level technical choices, then initialize, review, approve, or recheck the canonical workspace technical intent derived from an Active BRD, questionnaire, standards, classifications, and exact graph baselines. Use after BRD approval and before change-dossier creation or bounded planning.
---

# Govern Technical Intent

## Workflow

1. Confirm `cis brd status --workspace <workspace>` reports Active, valid, and current.
2. Run `cis technical-intent questions init`. For an existing implementation, retain evidence-supported `Derived` answers with confidence and provenance; present only unresolved or intentionally overridden choices for human input. For a greenfield project, present the complete `TI-Q-*` set. Record each human answer with `cis technical-intent questions answer`; advisory directions are not answers.
3. Build and strictly validate the workspace graph.
4. Run `cis technical-intent init --workspace <workspace> --format agent` to generate or reconcile the deterministic scaffold from the completed questionnaire, Active BRD, classifications, standards, and graph builds.
5. Review the generated choices, component map, BRD-derived `TI-MOD-*` module tree and responsibility profiles, `TI-INT-*` integration-point catalog, business baseline, technical surface, standard provenance, and architecture guidelines without broadening the BRD or silently contradicting a human answer.
6. Confirm that each business capability has one owning module; each module states purpose, BRD coverage, owned and excluded responsibilities, inputs, outputs, data/state, security, failure/recovery, and verification. Merge or split candidates only with preserved requirement traceability and explicit ownership migration.
7. Confirm every cross-module, external-system, identity, persistence, model, event/file, operator, and runtime handoff records owner, direction, contract/data, authentication and authorization, compatibility, consistency, idempotency, timeout/retry, failure/recovery, observability, and test expectations. Exact endpoints and schemas belong in linked canonical reference dictionaries.
8. Complete architecture boundaries, data and consistency, contracts, security/privacy, operations/recovery, quality evidence, and delivery constraints. Preserve substantive human-authored sections.
9. Record later bounded unresolved choices in `Open technical decisions` with stable `TI-DEC-*` identity, gate, status, and rationale. Agents may propose options but never select or defer them without explicit human authority.
10. Run `cis technical-intent validate` and `status`. Resolve every placeholder, unresolved decision, stale questionnaire/BRD/standard digest, and participant-baseline drift.
11. Run `cis technical-intent approve --reviewer <human> --reason <rationale>` only after explicit authority.
12. After approved implementation changes participant graph IDs, run `cis technical-intent refresh`; request renewed approval only for material semantic change.
13. Rebuild and strictly validate the workspace graph after approval.
14. Run `cis solution-design init`; review and approve the overall architecture and component sheet as one bundle. Then complete and approve `cis ui-direction` before backlog, feature-design, change, or plan commands.

## Guardrails

Preserve managed baseline and derived-evidence markers and identities. Do not turn ambiguous evidence or absence of code into a derived fact, record advisory questionnaire text without human action, treat Draft, Ready for Approval, or Stale as Active, approve on a user's behalf, edit approval hashes, or bypass the readiness gate.

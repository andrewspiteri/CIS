---
name: cis-govern-business-requirements
description: Discover possible BRDs, domain-equivalent product-design documents such as GDDs, and development feature specifications, create or reconcile the canonical workspace business requirements document, assess and trace source evidence, validate completeness and drift, and present approval readiness. Use for BRD intake, product-design intake, feature-spec absorption, review, currency checks, or workspace business-requirement governance.
---

# Govern Business Requirements

## Workflow

1. Confirm `.cis/workspace.yml` identifies exactly one `authority` repository. If not, dry-run `cis workspace init --root <documentation-root>` and request review before using `--yes`.
2. Run `cis graph build --workspace <workspace>` and `cis graph validate --workspace <workspace>` before intake.
3. Run `cis brd discover --workspace <workspace> --format agent`. Treat every found BRD, product-design document such as a GDD, or feature specification as unverified source evidence; absence creates no implied requirements.
4. Run `cis brd init --workspace <workspace> --title <title>`. Preserve the authority repository's canonical document and catalog entry.
5. During delivery, rebuild the affected repository graph after a feature specification is created or changed, then run `cis brd status`. If it is Stale or reports unassessed evidence, run `cis brd reconcile --workspace <workspace>`.
6. Review every source row. Set Assessment to `Adopted`, `Reference`, or `Rejected` and record rationale. For an Adopted feature specification, incorporate its business intent into the relevant BRD sections and cite its `BRD-SRC-*` ID in Traceability.
7. Rebuild workspace graphs after canonical edits, then run `cis brd validate` and `cis brd status`.
8. Present validation errors, warnings, participant baseline drift, unresolved sources, and approval readiness to the user.
9. Run `cis brd approve --reviewer <human> --reason <rationale>` only after the user explicitly authorizes that exact approval. Rebuild the authority graph after approval.
10. After technical intent is Active/current, run `cis brd backlog build`, review one-to-one functional-requirement coverage, repository routing, frontend types, dependencies, and global obligations, then validate and present approval readiness.
11. Run `cis brd backlog approve --reviewer <human> --reason <rationale>` only with explicit authority. An approved high-level item may then become a feature specification; it is not an implementation task.
12. Start only a dependency-ready item with `cis brd backlog start --item <HLT-ID>`. Complete the generated Draft specification and run `cis brd feature validate --item <HLT-ID>` until it is Ready for Approval.
13. Present the exact feature scope, validation result, and approval rationale. Run `cis brd feature approve --item <HLT-ID> --reviewer <human> --reason <rationale>` only with explicit human authority.
14. After feature approval, rebuild the graph and reconcile the now-eligible feature evidence into the BRD. Renew downstream technical-intent and backlog authority before creating a change dossier.

## Guardrails

Never infer currency from file existence, choose source dispositions, claim semantic absorption without updating BRD content and traceability, invent business requirements, approve on a user's behalf, or describe Review Required, Ready for Approval, or Stale content as Active. Do not edit managed candidate IDs, approval hashes, source hashes, baseline rows, or block markers. CIS may mark an approved BRD, backlog, or feature Stale from content/evidence drift; only explicit human approval may restore Active status. Feature approval accepts scope but never authorizes implementation.

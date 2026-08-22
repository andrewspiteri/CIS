---
applyTo: "docs/specs/business-requirements.md"
---

# CIS business requirements authority

- The canonical BRD belongs only to the repository registered with workspace role `authority`.
- Treat BRDs, domain-equivalent product-design documents such as GDDs, and development feature specifications found in authority or participant repositories as source evidence until a human assesses each source row.
- File existence, deterministic extraction, graph freshness, or agent review never proves business currency.
- Complete business outcomes, scope, actors, capabilities, requirements, constraints, success measures, traceability, and open questions through human review.
- Preserve CIS baseline and source block markers, candidate IDs, source and approval hashes, and participant graph identities.
- After creating or changing a feature specification, rebuild its repository graph and run `cis brd reconcile`; new or changed evidence invalidates prior approval.
- `Adopted` feature specifications must be incorporated into relevant BRD sections and cited by their managed source ID in Traceability.
- Agents may discover, draft, propose reconciliation edits, reconcile managed evidence, and validate. They may not select source assessments, invent stakeholder decisions, claim semantic absorption without corresponding BRD edits, or run `cis brd approve` without explicit user authorization for the reviewer and rationale.
- `Active` requires recorded human approval. Evidence drift may reduce it to `Stale`; automation may never restore `Active`.
- After BRD and technical-intent approval, use `cis brd backlog build` to create the reviewed high-level bridge. Do not jump directly from the BRD to executable tasks.
- After backlog approval, use `cis brd backlog start --item <HLT-ID>` for a dependency-ready outcome. Complete the generated Draft and run `cis brd feature validate --item <HLT-ID>` before presenting it for approval.
- Run `cis brd feature approve --item <HLT-ID> --reviewer <human> --reason <rationale>` only with explicit authority. Rebuild the graph and reconcile an approved feature into the BRD before change planning.
- High-level backlog approval authorizes feature-specification preparation only; detailed implementation work still requires a change dossier, impact review, and bounded plan.
- Feature approval accepts the detailed scope only; it does not authorize implementation, deployment, or release.

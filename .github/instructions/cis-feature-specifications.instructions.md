---
applyTo: "docs/specs/features/**/feature-specification.md"
---

# CIS feature-specification authority

- Preserve `cis.stable_id`, `cis.high_level_item`, `cis.brd_requirement`, `cis.backlog_item_hash`, `cis.product_definition_hash`, and approval metadata. The CIS controller owns the exact product-definition digest.
- Cover every affected repository and every public, customer, or backoffice frontend type recorded by the selected high-level backlog item.
- Use structured `FEAT-*` requirements with a bounded surface, frontend type, testable requirement, and acceptance criteria. Surface must be exactly `frontend`, `backend`, `full-stack`, `mobile`, `native`, `api`, `contract`, `data`, `security`, `delivery`, or `documentation`; frontend type must be exactly `public`, `customer`, `backoffice`, or `not-applicable`.
- Complete every required section explicitly; use a reasoned `Not applicable` statement rather than a placeholder.
- A generated scaffold is not a feature draft. Use `cis agent author feature --item <HLT-ID> --provider <provider> --actor <human>` for isolated evidence-bounded expansion, or author it manually, before validation.
- When `cis definition status` reports an open wizard session, finish the consolidated product-definition activation first. Never request feature approval for an unbound or stale product-definition baseline.
- Run `cis brd feature validate --item <HLT-ID>` and present the exact scope and validation result before requesting approval.
- Never run `cis brd feature approve` without explicit human reviewer identity and rationale. Approval accepts scope and may be carried by `cis plan derive` only through eligible deterministic impacts and the exact validated plan; it does not directly authorize implementation or release.
- After approval, rebuild the graph, reconcile the feature as BRD evidence, and refresh technical intent before change planning. Managed adoption of the unchanged approved authority feature must not trigger another BRD secondary review or revoke the consolidated product-definition baseline.
- For the reconciled current feature, prefer `cis plan derive` and do not request separate impact or plan approvals when it succeeds. Surface only exceptional low-confidence, deferred, truncated, conflicting, stale, or invalid results for human review.

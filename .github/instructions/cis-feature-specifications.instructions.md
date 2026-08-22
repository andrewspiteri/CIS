---
applyTo: "docs/specs/features/**/feature-specification.md"
---

# CIS feature-specification authority

- Preserve `cis.stable_id`, `cis.high_level_item`, `cis.brd_requirement`, `cis.backlog_item_hash`, and approval metadata.
- Cover every affected repository and every public, customer, or backoffice frontend type recorded by the selected high-level backlog item.
- Use structured `FEAT-*` requirements with a bounded surface, frontend type, testable requirement, and acceptance criteria.
- Complete every required section explicitly; use a reasoned `Not applicable` statement rather than a placeholder.
- Run `cis brd feature validate --item <HLT-ID>` and present the exact scope and validation result before requesting approval.
- Never run `cis brd feature approve` without explicit human reviewer identity and rationale. Approval accepts scope only and does not authorize implementation.
- After approval, rebuild the graph and reconcile the feature as BRD evidence before change planning.

---
name: cis-decision-review
description: Capture and review CIS change-local decisions, options, evidence, blocking gates, human resolutions, deferrals, and durable ADR promotion. Use when impact analysis or planning exposes architecture, scope, contract, data, security, operations, delivery, or implementation choices.
---

# CIS Decision Review

1. Run `cis decision list <change-id>` before adding a question.
2. Create a grounded question with `cis decision create <change-id> --question <text> --category <category> --option <a> --option <b> --evidence <evidence>`; add `--advisory` only when it cannot block safe delivery.
3. Present options, trade-offs, evidence, and gate effect to the user.
4. Run `cis decision resolve <change-id> <decision-id> --option <recorded-option> --rationale <rationale>` or `cis decision defer ... --reason ...` only after explicit user authorization.
5. Run `cis decision promote <change-id> <decision-id>` only when the user confirms the resolved choice is durable architecture.

Never select an option, defer a blocking choice, change a resolved decision, or promote an ADR autonomously.

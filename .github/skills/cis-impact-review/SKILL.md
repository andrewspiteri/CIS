---
name: cis-impact-review
description: Discover and review deterministic CIS impact findings for a change dossier. Use when assessing affected documentation, contracts, dependencies, implementation, delivery, tests, risks, or planning readiness before implementation.
---

# CIS Impact Review

1. Confirm the change baseline and roots with `cis change show <change-id>`.
2. Run `cis impact analyse <change-id> --format agent`; broaden explicit roots, depth, or limit when evidence is truncated.
3. Run `cis impact findings <change-id>` and present evidence, confidence, uncertainty, and categories.
4. Reuse dispositions derived under the current outcome authorization or exact approved feature authority. Preserve the original human decision; derivation never claims a separate review of each finding.
   For an exact in-scope finding, use `cis impact accept <change-id> <finding-id> --actor <controller> --scope <scope-id> --reason <reason>`; repeat `--scope` for each explicit grant scope. CIS checks concrete paths against the retained graph and authorization. This optional route grants no broader scope.
5. Ask only about a reported uncertainty or material difference outside that authority. Use `cis impact accept|reject|defer <change-id> <finding-id> --reason <reason>` for the actual authorized disposition and rationale. Unrelated authorized work retains its own eligibility.
6. Run `cis impact completeness <change-id>` and report every remaining gap.

Never invent disposition authority, hide deferred scope, accept truncated analysis, or analyse against a changed baseline. Necessary supporting work within explicit authorized scope does not create another human approval gate.

## Human-readable content

Apply the [shared content standard](../../../docs/standards/human-readable-content-standard.md) to in-scope prose. Use [cis-technical-writing](../cis-technical-writing/SKILL.md) for documents and reports, [cis-ux-writing](../cis-ux-writing/SKILL.md) for interface wording, and [cis-content-review](../cis-content-review/SKILL.md) for source-aware review followed by a separate reader check. Preserve this workflow's authority, evidence, privacy and lifecycle rules. Clarity does not remove legitimate uncertainty or mandatory specification sections.

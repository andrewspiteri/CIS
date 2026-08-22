---
name: cis-impact-review
description: Discover and review deterministic CIS impact findings for a change dossier. Use when assessing affected documentation, contracts, dependencies, implementation, delivery, tests, risks, or planning readiness before implementation.
---

# CIS Impact Review

1. Confirm the change baseline and roots with `cis change show <change-id>`.
2. Run `cis impact analyse <change-id> --format agent`; broaden explicit roots, depth, or limit when evidence is truncated.
3. Run `cis impact findings <change-id>` and present evidence, confidence, uncertainty, and categories.
4. Ask the user to disposition unresolved findings.
5. Run `cis impact accept|reject|defer <change-id> <finding-id> --reason <reason>` only for the exact user-authorized finding and rationale.
6. Run `cis impact completeness <change-id>` and report every remaining gap.

Never disposition findings autonomously, hide deferred scope, accept truncated analysis, or analyse against a changed baseline.

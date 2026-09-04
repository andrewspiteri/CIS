---
name: cis-tooling-evidence
description: Record and validate exact CIS routing, generation, testing, run, and usage evidence for changed files. Use before handoff, verification, or pull-request readiness checks.
---

# CIS tooling evidence

1. Add a `## CIS tooling evidence` section.
2. Record exact context or graph commands, generation decision, reconciled test command, and run ID.
3. Preserve the local usage ledger or state a specific bypass reason.
4. Run `cis agent evidence validate --evidence <path> --changed-file <path> --strict --format agent`.
5. Replace placeholders and resolve every warning before readiness is claimed.

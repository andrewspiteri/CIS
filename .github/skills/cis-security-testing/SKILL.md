---
name: cis-security-testing
description: Run and reconcile portable security scanners, inspect redacted evidence, optionally obtain a local-only AI triage summary, and preserve deterministic release gates.
---

# CIS Security Testing

## Workflow

1. Read the security-testing standard and the repository security-suite profile.
2. Run `cis security validate --strict` and `cis security exceptions validate --strict`.
3. Execute the repository workflow once; retain its first failure and bounded scanner logs.
4. Run `cis security reconcile --run <workflow-run-id>`.
5. Inspect exact unresolved findings and expiry-bound acceptances. Never reveal secret values.
6. Run `cis security summarise --run <workflow-run-id>` for advisory local triage, or add `--no-llm` when no model should be used.
7. Fix findings, run a new workflow attempt, and reconcile that distinct run. Do not overwrite previous evidence.
8. Before release, prove revision freshness and exact scanned image identity.

## Guardrails

- Scanner evidence and governed exceptions decide the verdict; model text never does.
- Do not send findings to a remote model through this skill.
- Do not add wildcard, indefinite, self-approved, or model-approved exceptions.
- Treat a successful command with a missing result as invalid evidence.
- Keep generated evidence under `.cis/local/security/` and do not commit it.

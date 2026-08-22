---
name: cis-standards-governance
description: Resolve, import, audit, author, or revise repository standards; validate stable normative rules and conformance mappings; and review enforcement gaps before governed implementation or completion.
---

# CIS Standards Governance

1. Read `.cis/repository.yml`, `docs/standards/`, and `docs/references/standards-conformance-matrix.md`.
2. Treat classification-selected PARR-derived standards as repository-owned canonical starters, not immutable upstream policy. Verify their provenance and applicability, then strengthen or supersede them through reviewed changes when repository architecture requires it.
3. Before implementation, run `cis standards applicable --target <target> [--stack <stack>] --format agent` for every affected surface. Read the returned canonical standards; do not rely on routing output alone.
4. When authoring a standard, start from `docs/templates/standard-template.md`. Keep it below `standards/`, register it as canonical `type: standard`, and assign an immutable document ID plus repository-unique stable rule IDs.
5. Express mandatory rules with `MUST` or `MUST NOT`, recommendations with `SHOULD`, and options with `MAY`. Make each rule independently testable.
6. Add every active rule to the conformance matrix. Prefer deterministic checks or architecture tests; use manual review where semantic judgment is necessary.
7. Treat `advisory-model` results as candidates only. Never report them as confirmed breaches or proof of compliance without deterministic evidence or human acceptance.
8. Record exceptions with the rule ID, human approver, rationale, bounded scope, expiry or review condition, and compensating controls. Never approve an exception yourself.
9. Run `cis standards validate --strict`, `cis standards conformance --gaps-only`, and `cis docs validate --strict` after changes. Rebuild the graph so standards remain queryable.
10. Before importing, run `cis standards import --source <path-or-url> --dry-run`. Use `--fix` only for staged structural repair and stable IDs on existing normative statements; it never invents semantics. Review the file, catalog, and default manual-review mappings, then rerun with `--yes` only when the canonical admission is intended.
11. Run `cis standards audit` after imports or material standard changes. Audit is local-first and may use a configured remote provider when local generation is unavailable; use `--no-llm` for deterministic-only review. Treat all model findings as advisory.
12. Use `cis standards audit --fix` only when reversible quarantine is intended. Inspect the preserved historical catalog and matrix records; never infer that quarantine resolves the underlying governance decision.
13. Run `cis standards patterns --strict` before using repository or extension inference patterns. Duplicate IDs are conflicts; never select a pattern by provider load order.
14. To discover observed conventions, build a fresh compiler graph and run `cis standards infer`. Review matches and counterexamples in `.cis/local/standards/inference/`; inferred candidates are not standards and cannot grant normative authority.

Keep policies, standards, specifications, and procedures distinct. A policy defines authority or required outcomes; a standard defines the expected way; a specification defines a precise contract; a procedure defines ordered steps.
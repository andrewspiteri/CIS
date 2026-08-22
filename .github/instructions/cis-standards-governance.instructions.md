---
applyTo: "**"
---

# CIS standards governance

- Resolve applicable active standards before implementation with `cis standards applicable --target <target> [--stack <stack>]` and read each returned canonical file below `docs/standards/`.
- Classification-selected PARR-derived defaults are repository-owned starter standards. Review their provenance and adapt them through canonical, human-reviewed changes rather than assuming PARR product choices apply unchanged.
- Treat policies as authority or outcome rules, standards as required ways of working, specifications as precise contracts, and procedures as ordered steps.
- Preserve immutable document IDs and repository-unique rule IDs; retired IDs are never reused.
- Use `MUST` and `MUST NOT` for mandatory rules, `SHOULD` for expectations requiring deviation rationale, and `MAY` for optional behavior.
- Map every active rule in `docs/references/standards-conformance-matrix.md` to deterministic, architecture-test, manual-review, advisory-model, or not-mapped enforcement.
- Never turn an advisory-model or heuristic candidate into a confirmed breach or compliance claim without deterministic evidence or explicit human review.
- Never approve a standards exception. Record the human approver, rationale, bounded scope, expiry or review condition, and compensating controls.
- After standard or conformance changes, run `cis standards validate --strict`, inspect `cis standards conformance --gaps-only`, run `cis docs validate --strict`, and rebuild the graph.
- Preview imports with `cis standards import --source <path-or-url> --dry-run`; canonical file, catalog, and conformance changes require `--yes`. Structural `--fix` acts on staged copies, may assign IDs to existing normative statements, and never invents semantics.
- Run `cis standards audit` after imports or material changes. It prefers local generation, may use a configured remote provider when local is unavailable, and becomes deterministic-only with `--no-llm`.
- `cis standards audit --fix` authorizes reversible quarantine only. It does not authorize semantic merges, conflict resolution, exception approval, or deletion of historical evidence.
- Validate the known-standard-pattern catalogue with `cis standards patterns --strict`. Repository patterns live as cataloged canonical Markdown below `docs/references/standard-patterns/`; duplicate IDs are blocking conflicts.
- Run `cis standards infer` only against a fresh compiler-backed graph. Treat matches, prevalence, and counterexamples as derived observations; an inferred candidate is not a standard and cannot be promoted or accepted without explicit human review.
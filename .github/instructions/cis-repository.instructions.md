---
applyTo: "**"
---

# CIS repository guidance

- When onboarding or reconciling a repository, use `.github/skills/cis-repository-bootstrap/SKILL.md`.
- When onboarding several repositories, use `.github/skills/cis-import-repositories/SKILL.md`; dry-run the whole batch before confirmation.
- For BRD intake or currency review, use `.github/skills/cis-govern-business-requirements/SKILL.md`; discovery never proves currency and approval is human-only.
- Run `cis repo init` with an explicit maintainer-selected `--root`; if it returns an error or collision, run `cis repo doctor` with the same `--repo` and `--root`.
- Read `.cis/repository.yml` before repository-wide work.
- Start with `docs/README.md`, `docs/catalog.yml`, and the repository profile before broad searches.
- Treat `docs/specs/product-intent-spec.md` and `docs/specs/technical-intent-spec.md` as foundational intent documents.
- Use the smallest matching `cis-*` skill before inventing a repository workflow.
- Use `.github/skills/cis-skill-governance/SKILL.md` after initialization, classification changes, imports, or skill edits; audit is local-first with configured remote fallback, and only explicit `--fix` authorizes reversible quarantine.
- Use `.github/skills/cis-file-index/SKILL.md` and `cis index find` before broad source searches; cards are non-authoritative and disposable.
- Use `.github/skills/cis-feedback-loop/SKILL.md` to review automatic local usage, possible token savings, repeated failures, and compact-output opportunities.
- Use `.github/skills/cis-graph-context/SKILL.md` before broad repository searches or impact analysis; never edit `.cis/local/` derived state.
- Use `.github/skills/cis-change-dossier/SKILL.md`, `cis-impact-review`, `cis-decision-review`, and `cis-bounded-planning` for reviewed change delivery.
- Treat `impact accept|reject|defer`, `decision resolve|defer|promote`, `plan approve`, and `change close` as explicit human-authority commands.
- Inspect callers, contracts, tests, configuration, permissions, events, and operational effects before changing behavior.
- Update contract and domain-behavior references in the same change as implementation.
- For API changes, use `.github/skills/cis-api-contract-governance/SKILL.md`; run `cis api discover`, strict validation, compatibility diff when baselined, then rebuild the graph.
- Record durable technical choices as ADRs and link affected specifications and references.
- Keep generated discoveries marked as unverified until a maintainer reviews them.
- Never silently overwrite human-authored canonical documentation or claim checks that were not run.
- Update affected specifications and references in the same change, then run strict documentation validation.

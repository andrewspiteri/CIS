---
name: cis-skill-governance
description: Inventory, validate, repair, import, and audit portable repository skills without silently replacing or disabling human-managed guidance.
---

# CIS Skill Governance

## Purpose

Keep the repository's agent skills valid, discoverable, non-duplicative, and free of unresolved instruction conflicts.

## When to Use

Use after repository initialization, classification changes, skill imports, or edits beneath `.github/skills/`, and when agents receive overlapping instructions.

## Workflow

1. Run `cis skills inventory --repo <repository> --format agent` to establish the installed set.
2. Run `cis skills validate --repo <repository> --strict --format agent`.
3. If validation reports only safely repairable missing YAML fields or headings, review and run `cis skills validate --fix --strict`; otherwise correct the source manually.
4. For preexisting bundles, preview `cis skills import --source <path-or-url> --dry-run`; import with `--yes` only after the plan is reviewed and authorized.
5. Run `cis skills audit --repo <repository> --format agent`. Audit prefers a local model and falls back to a configured remote provider; use `--no-llm` when skill content must stay local.
6. Inspect `.cis/local/skills/audit.md` and the named skills before deciding whether to consolidate, specialize, or clarify them. Use `--strict` when unresolved findings must gate the workflow.
7. Use `cis skills audit --fix` only when quarantine of redundant deterministic duplicate copies and both sides of evidence-backed conflicts is intended. Overlaps remain active.
8. Re-run validation and audit after restoration or any approved canonical edit.

## Guardrails

Treat model findings as advisory candidates, not approval authority. Running audit without `--no-llm` authorizes bounded candidate content to use a configured remote provider when local generation is unavailable. Only explicit `--fix` authorizes quarantine moves; it never overwrites or deletes bundles. Preserve complete imported bundles and resolve same-name content conflicts manually.

## Output Expectations

Report validation state, applied safe fixes, import decisions, deterministic duplicates, advisory overlaps or conflicts, remote fallback, quarantine moves, omitted unsubstantiated model findings, and the exact human decision still required.

## Related Files

Read the `cis skills inventory`, `validate`, `import`, and `audit` command manuals. Treat `.cis/local/skills/` as disposable derived state.

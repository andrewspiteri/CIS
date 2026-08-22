---
title: "cis skills audit"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-15"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-skills-audit
---

# `cis skills audit`

Reviews installed repository skills and isolates exact duplicates, responsibility overlaps, and contradictory instructions in disposable local audit reports. It changes no skill unless explicit `--fix` authorizes reversible quarantine moves.

## Synopsis

```text
cis skills audit [--repo <path>] [--strict] [--no-llm] [--fix]
  [--model <model>] [--max-pairs <1-1000>]
  [--format <human|json|agent>]
```

| Option | Required | Default | Effect |
| --- | --- | --- | --- |
| `--repo <path>` | No | Current directory | Selects the initialized repository. |
| `--strict` | No | `false` | Makes every deterministic or model finding fail unless quarantine resolves it. |
| `--no-llm` | No | `false` | Skips all model review and runs deterministic checks only. Without this option, audit prefers local generation and may fall back to an available configured remote provider. |
| `--fix` | No | `false` | Quarantines eligible duplicate or conflicting skill bundles under `.github/skills-quarantine/`. |
| `--model <name>` | No | Provider default | Requests a specific model. A provider offering that model is preferred, with local providers first. |
| `--max-pairs <count>` | No | `100` | Bounds candidate pairs sent to semantic review; valid range is 1 through 1000. |
| `--format <format>` | No | `human` | Selects human, JSON, or line-oriented agent output. |

## Audit process

1. Resolve the initialized repository and run normal structural skill validation.
2. Compare normalized instruction bodies and trigger descriptions deterministically.
3. Select a bounded set of high-similarity trigger-description pairs, or pairs with the same primary action and enough shared responsibility terms. Boilerplate headings and generic body vocabulary do not create candidates.
4. Detect an available text-generation provider automatically unless `--no-llm` is set. Prefer local generation; if unavailable, select an available configured remote provider.
5. Ask the selected model to classify each supplied pair independently as duplicate, overlap, conflict, or no finding.
6. Accept model findings only for supplied pairs when confidence is sufficient, two evidence statements are grounded in the respective skills, and the claimed responsibility is material. Persist the combined result beneath `.cis/local/skills/`.
7. When `--fix` is present, preflight and move eligible bundles into `.github/skills-quarantine/` without overwriting an existing quarantine.

Reviewing one pair per prompt prevents models from mixing evidence between unrelated skills. Model output is capped at 384 tokens and local requests time out after 45 seconds, so an unhealthy local model degrades to partial advisory review rather than blocking the workflow. A conflict requires mutually exclusive instructions that can apply in the same situation; complementary, orchestrator/specialist, verification, documentation, and different test scopes are not conflicts. Every model finding requires separately grounded evidence from both skills; a conflict additionally requires a directive and a prohibition. Unsubstantiated classifications are omitted. Common trailing-comma JSON is accepted, while malformed output becomes a warning. Deterministic exact-body matches have priority over model output. A missing or failed model produces a warning and does not discard deterministic results. Model findings remain advisory unless the user explicitly invokes `--fix` or uses `--strict` as a gate.

Remote fallback transmits the bounded candidate content shown to the model: each skill's name, description, and up to 1,500 body characters. Use `--no-llm` when repository skill content must remain local.

## Quarantine policy

`--fix` is an explicit reversible isolation action:

- For a deterministic exact duplicate group, the alphabetically first skill remains active and the redundant copies are quarantined.
- For an evidence-backed conflict, both sides are quarantined because CIS has no authority to choose a winner.
- Overlap findings are never quarantined automatically.
- Every destination is checked before any move. Existing quarantine content is never overwritten.
- If a move fails, CIS attempts to roll back moves completed by that invocation and reports any rollback error.

Quarantined bundles remain complete beneath `.github/skills-quarantine/<name>/`. Active inventory, validation, import discovery, repository classification, graph extraction, and routing-card indexing exclude the quarantine tree, so quarantine disables agent routing without deleting evidence. Restoration is a deliberate manual move followed by strict validation and another audit.

## Derived outputs

| Path | Purpose |
| --- | --- |
| `.cis/local/skills/audit.json` | Machine-readable counts, stable finding IDs, involved skills, method, confidence, evidence, and warnings. |
| `.cis/local/skills/audit.md` | Human-readable grouping of conflicts, duplicates, and overlaps. |
| `.github/skills-quarantine/<name>/` | Complete inactive skill bundles moved by an explicit `--fix`. |

The two `.cis/local/` reports are disposable derived state and are replaced on rerun. Quarantined bundles are retained repository content and are not disposable.

## Exit codes

| Code | Meaning |
| --- | --- |
| `0` | Audit completed; advisory duplicate, overlap, or model conflict candidates may be present outside strict mode, or eligible strict findings were quarantined. |
| `2` | Repository, structural validation, pair limit, output format, quarantine destination, move, or rollback is invalid. |
| `5` | A confirmed error-severity finding exists, or `--strict` made any audit finding blocking. |

## Examples

Run deterministic checks and automatic local-first model review:

```powershell
cis skills audit --repo . --format agent
```

Use deterministic checks only and fail on every finding:

```powershell
cis skills audit --repo . --no-llm --strict
```

Quarantine eligible duplicate or conflicting bundles:

```powershell
cis skills audit --repo . --fix
```

Use a named model and a smaller review budget:

```powershell
cis skills audit --model qwen2.5-coder:1.5b --max-pairs 40
```

## Related commands

- [`cis skills inventory`](cis_skills_inventory.md)
- [`cis skills validate`](cis_skills_validate.md)
- [`cis skills import`](cis_skills_import.md)
- [`cis repo doctor`](cis_repo_doctor.md)

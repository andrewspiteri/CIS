---
title: "cis standards audit"
type: manual
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-16"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-standards-audit
---

# `cis standards audit`

Finds duplicate, overlapping, and conflicting standards while preserving the authority boundary between evidence and governance decisions.

```text
cis standards audit [--repo <path>] [--strict] [--no-llm] [--fix] [--model <name>] [--max-pairs <1..1000>] [--format <human|json|agent>]
```

Deterministic analysis detects repeated normalized bodies, materially identical normalized rule text, and rule-ID reuse. Candidate semantic pairs are selected from standard titles and declared targets, bounded by `--max-pairs`, and reviewed independently. Full body boilerplate does not make two standards candidates by itself. Unless `--no-llm` is supplied, provider routing prefers an available local model and may use a configured remote provider when local generation is unavailable.

Model classifications are advisory candidates and cannot prove a breach or compliance. Every accepted model finding must cite an exact rule ID and a grounded paraphrase from each standard. Duplicate and overlap findings must also have materially similar cited rule content; a shared target or broad topic alone is not a finding. Conflict findings require evidence of contradictory normative requirements. Responses may contain JSON comments or trailing commas without invalidating otherwise usable evidence.

Semantic responses are capped at 512 output tokens. Local requests time out after 45 seconds and preserve deterministic findings when generation is unavailable or partial.

`--strict` fails for unresolved findings. `--fix` authorizes reversible quarantine: redundant deterministic duplicates and evidence-backed conflicts move to `<documentation-root>/standards-quarantine/`; their catalog entries become historical `quarantined-standard` records and former matrix rows remain as quoted history. The command never merges rules, chooses a preferred policy, approves an exception, or deletes evidence.

Audit evidence is written to `.cis/local/standards/audit.json` and `audit.md`. Exit code `0` means clean or fully quarantined under the selected strictness, `2` means audit/quarantine could not run safely, and `5` means findings remain under strict mode.

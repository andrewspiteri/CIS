---
name: cis-diagnostics-analysis
description: Validate and inspect bounded redacted text or structured diagnostics. Use for evidence-led diagnosis of test, browser, workflow, container, or application failures.
---

# CIS diagnostics analysis

1. Read `docs/references/diagnostics-profile.md` and run `cis diagnostics doctor --format agent`.
2. Inspect `summary`, then narrow `events` by source, level, text, or time.
3. Run `analyse` to group stable redacted fingerprints without replacing raw evidence.
4. Use `export` for a bounded normalized JSONL handoff.
5. Never enable a sensitive raw source; create a sanitized export first.

---
title: "cis standards import"
type: manual
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-15"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-standards-import
---

# `cis standards import`

Discovers, stages, validates, and admits existing standards into canonical repository governance.

```text
cis standards import --source <path-or-url> [--source <path-or-url>...] [--repo <path>] [--dry-run] [--yes] [--fix] [--strict] [--format <human|json|agent>]
```

Sources may be Markdown files, directories, ZIP archives, GitHub repository or `tree/<ref>/<path>` URLs, or direct HTTP(S) ZIP URLs. Directory and archive discovery selects `*-standard.md` files and Markdown declaring `type: standard`; dependency, build, Git, CIS-local, and quarantine directories are excluded. Downloads and archive expansion are bounded, and escaping archive paths are rejected.

The command validates staged content before touching canonical state. `--fix` can add missing frontmatter fields and required section headings, and can assign deterministic stable IDs to existing uppercase `MUST`, `SHOULD`, or `MAY` statements when a legacy standard has no declared rule IDs. It never changes the source or invents normative semantics. `--strict` rejects inferred or repaired metadata warnings.

Every admitted standard is written below the configured `standards/` directory, registered as canonical in `catalog.yml`, and given conservative `manual-review` conformance rows for rules without mappings. Divergent destination content or catalog identity collisions stop the entire import. `--dry-run` never writes; without `--yes`, a non-empty change returns confirmation-required.

Exit codes are `0` for dry-run, unchanged, or imported; `2` for invalid sources or standards; `3` when confirmation is required; and `4` for collisions. Import provenance is recorded in disposable `.cis/local/standards/imports.json` after application.

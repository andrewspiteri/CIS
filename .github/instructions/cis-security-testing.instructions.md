---
applyTo: "src/Cis.Modules.Security/**,tests/Cis.Modules.Security.Tests/**,docs/specs/security-testing-and-evidence-spec.md,docs/standards/security-testing-standard.md,docs/references/security-suite-profile.md"
---

# CIS security module instructions

- Preserve canonical Markdown and derived `.cis/local/security/` separation.
- Add malformed, empty, stale, redaction, path-escape, and exception-expiry tests for adapter or reconciliation changes.
- Never place raw secret evidence in a test failure, normalized finding, log, prompt, snapshot, or committed fixture.
- Keep local AI summaries advisory and prove they cannot alter deterministic verdicts.
- Preserve stable human, JSON, and agent output and documented exit codes.

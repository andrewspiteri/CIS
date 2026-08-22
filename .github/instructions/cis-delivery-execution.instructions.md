---
applyTo: "**"
---

# CIS delivery execution authority

- Canonical Markdown remains authoritative; `.cis/local/` is disposable.
- Model output, workflow exit codes, and agent results never grant approval or completion.
- Remote model use requires a reviewed route and explicit authorization.
- Reject stale agent envelopes and record imported results only as evidence.
- Workflow commands run without a shell and resume only an unchanged definition.
- Verification acceptance requires a human reviewer and rationale.
- Diagnostics may read only enabled, repository-relative, non-sensitive evidence.
- Learning proposals cannot self-apply.
- Run `cis repo doctor` when initialization or repository configuration fails.

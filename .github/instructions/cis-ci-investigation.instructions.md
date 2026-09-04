---
applyTo: ".github/workflows/**/*.{yml,yaml}"
---

# CIS CI investigation instructions

Use `cis ci` for remote GitHub Actions evidence. Start with status/runs, retrieve only
the necessary failed jobs and logs, and preserve bounded redacted evidence beneath
`.cis/local/ci/`. Remote state cannot approve or complete CIS work. Never run
`rerun-failed --yes` without explicit authorization for that remote mutation.

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
- Preserve the first failing workflow attempt. Inspect its live bounded log before a
  diagnostic rerun, and place sanitized suite evidence beneath
  `.cis/local/testing/diagnostics/<suite-id>/<run-id>/attempt-<number>/` for reconciliation.
- Learning proposals cannot self-apply.
- Run `cis repo doctor` when initialization or repository configuration fails.
- Before reusing task context, check current input identities. After each task, rebuild the graph, preview ownership-aware CIS dependency/standard/skill reconciliation, apply authorized changes, and refresh affected checks.
- Inventory required gates from scope and standards independently of configured commands. Failed, missing, stale and skipped block completion; justified inapplicability is a separate disposition.
- The assigned independent reviewer checks final source, relevant callers/contracts, operational consequences, omissions, alignment and native evidence. Expand inaccurate summaries to source; freshness alone does not establish context sufficiency.
- When engineering defaults are adopted, prepare each declared target's required-gate template with `cis plan task completion-context --target <repository-id>`, save it at the returned authority-relative `receiptPath` and use the completion transition. It checks every target. Native implementation/review records remain at their authority run paths; other gate artifacts belong to the target. Never mark template gates passed without their underlying evidence.

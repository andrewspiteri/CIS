---
applyTo: "**"
---

# CIS delivery execution authority

- Outcome authorization in `authorization.json`, canonical task state in `plan.json` and the validated task-specific contract govern outcome-authorized work; generated proposal/plan views cannot override them. Existing legacy Markdown authorities and protected approved content retain their roles. Use supported commands for managed changes. Local graphs, indexes and other routing caches are derived; retained accounting, reservations, settlement and recovery state under `.cis/local/` are protected operational facts, not disposable caches.
- Model output, workflow exit codes, and agent results never grant approval or completion.
- Remote model use requires a reviewed route and explicit authorization.
- Reject stale agent envelopes and record imported results only as evidence.
- Workflow commands run without a shell and resume only an unchanged definition.
- Final dossier acceptance through `cis verify finalize` requires the actual human reviewer and rationale; routine checks and review readiness proceed under their existing authority.
- Diagnostics may read only enabled, repository-relative, non-sensitive evidence.
- Preserve the first failing workflow attempt. Inspect its live bounded log before a
  diagnostic rerun, and place sanitized suite evidence beneath
  `.cis/local/testing/diagnostics/<suite-id>/<run-id>/attempt-<number>/` for reconciliation.
- Learning proposals cannot self-apply.
- Run `cis repo doctor` when initialization or repository configuration fails.
- Before reusing task context, check material input identities. Retain compatible graph, dependency, standard, skill, review and verification assessments; refresh only affected or unresolved portions, preserve human customizations and adopted alternatives, and apply authorized updates. Rebuild a stale affected graph or preview relevant reconciliation when needed; task completion or bookkeeping alone does not require replay.
- Inventory required gates from scope and standards independently of configured commands. Failed, missing, stale and skipped block completion; justified inapplicability is a separate disposition.
- The assigned independent reviewer applies the promised-workflow checks in `.github/skills/cis-validate-completion/SKILL.md` to accepted behavior, final source, relevant callers/contracts, operational consequences, omissions and actual evidence. Check legitimate resolution and continuation as well as required denial. Expand inaccurate summaries to source; freshness alone does not establish context sufficiency. Use the existing review, with required corrections and optional advice kept distinct.
- When engineering defaults are adopted, prepare each declared target's required-gate template with `cis plan task completion-context --target <repository-id>`, save it at the returned authority-relative `receiptPath` and use the completion transition. It checks every target. Native implementation/review records remain at their authority run paths; other gate artifacts belong to the target. Never mark template gates passed without their underlying evidence.

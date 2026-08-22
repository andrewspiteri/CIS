---
name: cis-delivery-execution
description: Execute governed CIS delivery through AI routes, deterministic templates, resumable workflows, portable agent envelopes, verification, diagnostics, and reviewed learning.
---

# CIS delivery execution

1. Read `docs/references/ai-routing-profile.md`, the applicable change plan and task,
   repository delivery policy, and command manual.
2. Prefer `cis generate` and `cis workflow` before model generation.
3. Never pass `--allow-remote` without explicit authorization for the exact content.
4. Use `cis agent prepare` for one ready task. An imported result is evidence, not completion.
5. Record exact checks with `cis verify evidence`; validate before human acceptance.
6. Read only enabled, non-sensitive diagnostic sources.
7. Learning proposals require human review before canonical promotion.
8. After repository/configuration failures, run `cis repo doctor` and follow its evidence.

---
applyTo: "**"
---

# CIS AI model qualification authority

- Provider health and model availability are not task-class approval.
- Canonical routes, datasets, and approvals live under `docs/references/`; derived evidence lives under `.cis/local/ai/qualification/`.
- Approval requires runtime probe, benchmark, prompt regression, a named human reviewer, rationale, and `--yes`.
- Generated text is hashed but not retained. Deterministic code calculates pass or failure.
- Provider-name conflicts fail closed, and remote use requires explicit authorization for the exact content.
- Explain a route before selection; never silently fall back to an unapproved model.

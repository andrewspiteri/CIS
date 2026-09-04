---
name: cis-policy-impact
description: Analyse surfaces governed by a changed policy, standard, ADR, or architecture rule. Use before creating remediation work from governance changes.
---

# CIS policy impact

1. Declare explicit `targets:` using backend, frontend, documentation, infrastructure, security, api, testing, verification, release, or repository-governance.
2. Run `cis impact policy analyse --policy <path> --strict --format agent`.
3. Review the bounded reports beneath `.cis/local/impact/policy/`.
4. Treat candidates as routing input, not proof that a code change is required.
5. Create governed findings only after reviewing source evidence.

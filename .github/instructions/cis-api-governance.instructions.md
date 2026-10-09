---
applyTo: "**/*.{cs,ts,tsx,js,jsx,json,yml,yaml}"
---

# CIS API governance instructions

Before changing an endpoint, read `docs/specs/api-design-and-governance-spec.md`,
`docs/references/api-governance-profile.md`, and the affected API, permission,
and Problem Details rows. Preserve the exposure and trusted-scope boundary.

After an API change:

1. Run `cis api discover --repo .`.
2. Run `cis api validate --repo . --strict` and resolve every error and warning.
3. Run `cis api diff --repo .` when a governed OpenAPI baseline exists.
4. Rebuild and validate the context graph.
5. If discovery or repository configuration fails, run `cis repo doctor --repo .`.

Do not edit `.cis/local/api/`, treat OpenAPI as the only inventory, accept caller-selected
trusted scope, or bypass `PUBLIC-ENDPOINT-CACHE` for unauthenticated endpoints.
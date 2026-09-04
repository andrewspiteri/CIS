---
applyTo: "**/*.{cs,fs,vb,ts,tsx,js,jsx,swift,kt,kts,json,yml,yaml,csproj,fsproj,vbproj,props,targets,toml,md}"
---

# CIS reference governance instructions

Before changing a configuration key, permission, command, event, workflow state,
invariant, projection, Problem Details identity, package, route, module boundary, entity,
or relationship, inspect its canonical row beneath `docs/references/`.

After changing a governed surface:

1. Run `cis references discover --repo .`.
2. Preview additive canonical rows with `cis references reconcile --repo .`; apply with `--yes` only after review.
3. Run `cis references validate --repo . --strict` and resolve every finding.
4. Run `cis references diff --repo . --base <delivery-baseline>`.
5. Update canonical Markdown only from reviewed facts; never copy derived state into it blindly.
6. If discovery fails, run `cis repo doctor --repo .`.

Do not edit `.cis/local/references/`, resolve provider conflicts by load order, or remove
a canonical row merely because deterministic source discovery did not find it.

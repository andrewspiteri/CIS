---
title: "cis references discover"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-10-07"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-references-discover
---

# `cis references discover`

Extract deterministic source identities and correlate them with canonical reference tables.

```text
cis references discover [--repo <path>] [--format <human|json|agent>]
```

The idempotent normalized result is written beneath `.cis/local/references/`. Canonical
Markdown is never changed.

Package discovery includes literal `PackageReference` declarations in MSBuild
`.props` and `.targets` files, using the declaring filename as the component identity
(for example, `directory-build`). These are declared shared build dependencies;
discovery does not evaluate imports or conditions. A `PackageVersion` entry alone
does not establish an installed dependency.

After upgrading, repositories with these shared declarations may need additional
catalogue rows before strict validation passes. This literal discovery does not
expand `GlobalPackageReference`, conditional imports or MSBuild properties. Files
with the same declaring filename share a component identity; inspect their source
paths when reviewing the inventory.

---
title: "cis references validate"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-references-validate
---

# `cis references validate`

Validate provider conflicts, canonical identities, source correlation, and evidence paths.

```text
cis references validate [--strict] [--no-refresh]
  [--repo <path>] [--format <human|json|agent>]
```

Without `--no-refresh`, source discovery runs first. Strict mode makes every unresolved
warning a failing governance result.

Rows use their complete identity: entity and field, workflow and state, relationship
endpoints and relationship name, configuration owner and path, package and component,
or route, component and platform. The optional `Repository` column scopes each identity
to its owner. Repeating an ID in another owned repository is valid; repeating the full
identity in the same repository remains an error. Punctuation and escaped table pipes
are preserved when comparing identities.

Participant evidence in an authority dictionary is resolved relative to that registered
product-owned repository. Blank, unknown or dependency repository IDs fail validation.
Evidence cannot escape its repository or use linked paths. Classification descriptions
such as `@azure/functions package dependency` are labels, not file locators.

Repeated evidence locators share one existence and symlink/junction check per resolved
repository/path during this validation. The cache ends with the command: subsequent calls
recheck deleted, restored or replaced paths. Row validation and diagnostics remain unchanged.

Source extraction remains local to `--repo`. A participant row cannot satisfy an authority
source observation. Current (non-Draft) foreign rows receive an explicit warning that their
source correlation must be validated in the participant; checking their paths does not prove
their implementation matches. Strict mode continues to fail on such unresolved warnings.

Normalized reference state uses schema 2. Default validation regenerates older disposable
state. With `--no-refresh`, run `cis references discover` first if state uses the old schema.

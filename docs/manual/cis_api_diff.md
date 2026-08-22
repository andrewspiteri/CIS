---
title: "cis api diff"
type: manual
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-14"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-api-diff
---

# `cis api diff`

Compares the current OpenAPI JSON document with every governed supported baseline
using the default forward-transitive compatibility model.

```text
cis api diff [--repo <path>] [--baseline <path>] [--current <path>] [--format <human|json|agent>]
```

`--current` defaults to the current document in the API governance profile. Without
`--baseline`, every configured baseline is compared; supplying `--baseline` is an
explicit single-baseline override for investigation. Comparison covers removed operations,
new required request fields or parameters/headers, removed response fields/statuses,
response nullability narrowing, security changes, and enum contraction/expansion.

Forward-transitive means the current contract must remain compatible with every
supported baseline in the same major-version line, not merely the latest predecessor.
Agent and JSON output include one comparison summary per baseline.

Breaking findings produce exit `5`; missing or unreadable documents produce exit `4`.
Findings do not authorize a breaking change: introduce a new major version and record
consumer migration unless the classification is corrected with evidence.

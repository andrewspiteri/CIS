---
title: "cis api diff"
type: manual
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-10-05"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-api-diff
---

# `cis api diff`

Compares the current OpenAPI JSON document with every governed supported baseline
using the default forward-transitive compatibility model.

```text
cis api diff [--repo <path>] [--baseline <path>] [--current <path>] [--summary] [--format <human|json|agent>]
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

## Nested schemas and coverage

Comparison follows local schema references, nested object properties and array items.
Property evidence uses escaped slash-separated paths. Recursive references terminate
with a coverage limitation; composed schemas and unsupported constraints also require
review. Nullable semantics, overlapping request/response enum paths, and aggregated
response variants also remain partial. Removing a required response guarantee is
reported as breaking. `coverageComplete=false` and `coverageLimitations` remain visible in every
format. Partial coverage returns exit 4 unless an observed breaking change returns 5.
Zero findings with partial coverage is not proof of compatibility.

Use `cis api diff --summary --format agent` (or `json`) for totals, baseline results
and coverage limits without individual findings. `omittedFindings` records the count;
omit `--summary` to retrieve full findings. Summary mode never changes the exit code.

Repository configuration errors return `2`. A complete comparison with only
non-breaking or potentially-breaking findings returns `0`; inspect its `status`
and findings to distinguish `compatible` from `review`. Coverage flags describe
schema traversal; always inspect `errors` and the exit code as well.

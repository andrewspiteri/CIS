---
title: "cis technical-intent approve"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-16"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-technical-intent-approve
---

# `cis technical-intent approve`

```text
cis technical-intent approve --reviewer <human> --reason <rationale>
  [--workspace <path>] [--format <human|json|agent>]
```

Records explicit human approval only when validation is complete and current. It refreshes
the BRD/participant baseline, records reviewer, timestamp, rationale, and a normalized
content digest, changes the catalog and document to Active, and never resolves decisions.
Rebuild and strictly validate the workspace graph after approval. Missing authority exits
`2`; incomplete content exits `5`.

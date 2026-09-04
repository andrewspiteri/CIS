---
title: "cis agent revise brd"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-31"
review_cadence: "on command or provider-policy change"
cis:
  stable_id: change-impact-studio:manual:cis-agent-revise-brd
---

# `cis agent revise brd`

Applies an approved BRD review-disposition set through a bounded agent provider.

```text
cis agent revise brd --review <review-run-id> --provider <id> --actor <human>
  [--transport <name>] [--timeout-seconds <30..86400>]
  [--approve-requests] [--repo <path>]
  [--format <human|json|agent>]
```

The command requires a complete human-approved `cis brd review` record with at least one
approved finding. The implementation provider must differ from the provider that produced
the source review. CIS creates an isolated Git scratch repository and permits edits only
to the canonical BRD.

The provider receives each finding's exact approved recommendation text as implementation
scope and legacy rejected recommendations as explicit guardrails. Its structured result must
name the source review run and contain every approved `BRD-REV-*` identity exactly once. CIS rejects the result if
the canonical BRD or human decisions changed concurrently, another file changed, a finding
identity is missing or added, or protected frontmatter, managed blocks, or existing human
question answers changed.

Source identities, repository/path values, hashes, assessments, ordering, and provenance
remain protected. The rationale cell of an existing source row may change only when an
approved recommendation explicitly requires that exact remediation. A successful provider
result that fails the deterministic copy-back gate remains retained; after the gate or
candidate is corrected, repeating this command revalidates and applies that exact run without
another provider execution.

On success CIS copies the one-file revision back, records the revision run and revised BRD
digest in the canonical disposition document, and leaves BRD lifecycle authority unchanged.
Run `cis agent review brd` through a provider different from this reviser for the required
closure-only verification. The verifier receives the applied disposition record and may report
only missed approved scope, introduced legacy-rejected scope, a regression caused by the
bounded revision, or protected evidence/scope drift. It must not reopen broad completeness,
repeat pre-existing findings, or propose unrelated improvements.

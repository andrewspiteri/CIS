# Imported BRD layout qualification

Historical qualification record: candidate identities, commands and unresolved states below describe that checkpoint. For the completed Windows .NET milestone and current trial instructions, use the [readiness record](engineering-defaults-trial-readiness.md) and [trial guide](engineering-defaults-dotnet-trial-guide.md). Earlier receipts and findings remain retained.

CIS now supports an explicit document-owned heading map for imported BRDs whose business
requirements span domain and lifecycle sections. The map preserves the authored narrative,
keeps the same requirement identities for validation and backlog generation, and is covered
by the approval content digest. Applying it requires the exact preview hash and leaves the
BRD Review Required.

The new `brd layout` command reports its selections, extracted requirement IDs and remaining
unmapped section roles. It accepts the plain bold-title requirement form as well as the
existing dash-separated form. Continuation paragraphs and constraint tables remain attached
to their requirement. Managed provenance, comments and fenced examples are excluded.
Ambiguous, missing or overlapping heading selections fail closed. Open questions retains
its own canonical workflow; a later activation register cannot be mapped to it.

The [command manual](../docs/manual/cis_brd_layout.md) explains preview/apply, section scope,
limits and graph/reconciliation steps. The repository BRD skill and its generated starter
both route agents to this workflow without granting business authority.

## Review and evidence

Independent review requested two corrections: exclude digest-excluded managed provenance
from mapped business content, and preserve mappings across supported frontmatter delimiter
whitespace. Both are corrected and have native xUnit regressions. Independent closure review completed with no remaining blocking findings in the reviewed scope.
A separate manual-reader check produced two usage clarifications, which were incorporated.

Evidence is retained in `.cis/local/qualification/brd-layout/`. The first 16 focused cases
and expanded 19-case run are overlapping qualification, not additive test counts. The
initial definition-import filter selected zero tests and is not counted as a pass; the
corrected three-method selection passes. Host checks pass 17 cases and the generated-guidance
selection passes two. The full BRD run finished with **221 passed, one failed and zero skipped**.
The sole failure was a missing temporary initialization directory during fixture setup,
before the reconciliation behavior was tested. That exact case passed unchanged twice:
once alone and once in the final 20-case selection (19 layout cases plus the repeat).
The final build has zero warnings/errors. All 222 distinct cases have a latest passing
result, but the full run was **not clean** and the setup failure's cause is unresolved.
Both receipts are retained. Independent follow-up review found no concrete initialization
defect and judged a bounded isolated trial reasonable with this limitation disclosed.

## Boundaries

This correction does not declare semantic equivalence automatically, answer business
questions, qualify external providers, approve requirements, start application implementation
or replace the active CIS installation. The private trial's source document and detailed
review stay outside public repository files. The trial mapping is a separate concrete
review action; missing business-review closure remains visible after successful extraction.

## Installed candidate and trial result

Candidate **0.3.0-engineering.20261009.29** is installed at
`.cis/local/engineering-tool-rehearsal-29/cis.exe`. Its package SHA-256 is
`2c23136b54d1aff677815bbfd9eb401b743a192cf1b53157f15546d050bc9915`.
All 42 CIS/host assemblies and 62 reference files match the build/source; the installed
BRD assembly matches the tested copy. The first install attempt could not read the NuGet
user configuration in the sandbox; the permitted retry used the same local-only feed.
The active installation is unchanged.

The packaged command applied the exact reviewed mapping in the private trial. Native
extraction matches its source requirement inventory, and the entire original narrative
is still present unchanged. Acceptance scenarios and later activation decisions remain
separate. The trial's native graph, strict documentation and skill checks pass, and the
deterministic skill audit is clean. Authority initialization updates only the BRD skill
and ownership manifest; its repeat proposes no changes. Previously retained guidance
remains retained.

Native BRD validation now reports only a missing Open questions section. The document
is current and Review Required; no approval, source assessment or business answer was
invented. The private intake report records the remaining clarification points and their
required-before boundaries. Source review and reader checks are model-assisted, not human
acceptance. This candidate is qualified for the bounded local trial, with the retained
test setup failure described above; it is not a clean full-suite release qualification.

## Candidate 30: synthetic question-workflow correction

The supplied BRD is sample input for CIS testing only. Continuation uses a separate, clearly
labelled synthetic authority and synthetic answers; it does not authorize implementation
of the sample product. The original document and imported canonical BRD remain unchanged.

The rehearsal exposed hidden trailing evidence being parsed as open questions. Candidate
**0.3.0-engineering.20261009.30** excludes hidden/fenced content from question parsing,
preserves trailing managed evidence on answer writes, and rejects ambiguous interleaving
without writes. Independent review also caught a review-freshness regression in the first
fix. The correction preserves evidence in the review comparison so evidence changes remain
stale. Its new native regression failed against the first fix and passes after correction.

Final targeted qualification passes **39 BRD and three Agent cases**, zero failures/skips;
independent review of the CIS code and command manual found no remaining concrete defects in scope. Strict
source documentation passes. This is bounded qualification, not a clean full-suite rerun.
Candidate29's original setup failure and successful repeats remain historical evidence.

Package SHA-256 is `24c715285e40cfff9591a195162f36cca6e26ee30c7a5a48bcb4788160f4bfb8`.
The isolated executable is `.cis/local/engineering-tool-rehearsal-30/cis.exe`; 42 CIS/host
assemblies and 62 reference files match build/source, and its BRD assembly matches tests.
The active installation is unchanged.

The installed tool recognizes three synthetic questions, rejects unanswered and partial
states, records the selected synthetic answers and repeats without writing. Strict trial
graph/docs/skills checks pass. Answered structural validation passes, but document status
remains Review Required. Incorporation and independent review of the resulting synthetic BRD are still unperformed:
automatic approval review rejected external-provider execution pending explicit permission
to transmit the sample and bounded context. No transmission or provider success is claimed.
The rehearsal is incomplete; it will stop before business approval or downstream implementation.


## Candidate 31: synthetic question rehearsal complete

The maintainer explicitly authorized the bounded synthetic sample and answers for Codex
incorporation and Claude review. Codex completed the one-file revision, but candidate30
rejected copy-back because Git quoted the imported filename containing spaces. Candidate31
applied that same retained result through native checks without another provider execution.
A repeat returns `already-incorporated`. Historical provider receipts were not rewritten.

The correction reads NUL-delimited Git paths, preserving spaces and Unicode, and checks the
live one-file scope before copy-back, including older quoted-path receipts. Failed,
truncated or malformed Git inventories block verification. Repositories without Git use
bounded input hashes before and after execution; changed inputs invalidate read-only reviews.
A failed question incorporation reports `Applied=false`.

Independent code review and source-aware manual review completed with a ready verdict. A separate reader check
led to clearer wording about matching the current answers and BRD baseline before reuse.
The final native Agent suite passes **224 tests, zero failures or skips**, including all
11 focused cases. The initial full run failed 17 non-Git compatibility cases; it is retained
alongside the corrected clean run. A targeted build attempted while that earlier run held
assemblies open failed and was repeated after the run ended. These are not passing receipts.
Candidate29's historical BRD fixture failure is unchanged; no new full BRD-suite pass is claimed.

Candidate **0.3.0-engineering.20261009.31** is installed at
`.cis/local/engineering-tool-rehearsal-31/cis.exe`. Package SHA-256:
`e0e202e3345c160a94da276502b10fbb4a7ce6f881755a2a544538c59707db13`.
All 42 CIS/host assemblies and 62 reference files match build/source; the installed Agent
assembly matches the tested copy. The active CIS installation is unchanged.

The synthetic authority passes strict graph, documentation and skill checks. Its three
questions remain answered, and its 102 extracted requirement IDs match the prior inventory.
The original supplied document and imported BRD retain their recorded hashes. Structural BRD
validation is current and reports Ready for Approval; document status remains Review Required.

After the maintainer reset the provider limit, Claude completed independent review with a
ready recommendation and no findings. Native review freshness confirms that its reviewed
hash matches the current synthetic BRD. The reviewer checked the complete current document,
answer incorporation, acceptance scenarios and surrounding consistency. It did not receive
original-source reference evidence or a pre-revision diff, so no source-coverage or full
historical regression claim is made. Local validation was run separately after review.
The earlier quota failure remains retained as failed evidence.

The bounded question-workflow rehearsal is complete. This does not approve the BRD or
qualify the whole CIS release. Remaining Foundation human acceptance and separately scoped
synthetic workload/recovery acceptance remain in the [final-sweep handoff](engineering-defaults-work180-final-sweep.md#scope-and-next-steps).
No sample application implementation, merge, publication or active-tool replacement occurred.

Generic qualification receipts are under `.cis/local/qualification/brd-layout/`, including
`agent-paths-full-final-06.trx`, `question-paths-final-05.trx` and `candidate31-integrity.json`.
Private sample content and detailed provider receipts remain outside this public repository.

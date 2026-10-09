# Independent review dispositions

Historical qualification record: candidate identities, commands and unresolved states below describe that checkpoint. For the completed Windows .NET milestone and current trial instructions, use the [readiness record](engineering-defaults-trial-readiness.md) and [trial guide](engineering-defaults-dotnet-trial-guide.md). Earlier receipts and findings remain retained.

## WORK-180 tooling review — 7 October, candidate 23

Three fresh read-only Codex CLI reviews assessed the bounded final-sweep corrections. The final report is **ready**, with no actionable findings, and all 13 reviewed source hashes match. These advisory code reviews are separate from the still-missing native WORK-180 task assurance and final acceptance.

| Finding | Disposition |
| --- | --- |
| Non-Git authority inherited an ancestor checkout's HEAD from graph metadata | Corrected baseline selection to require the authority's own checkout; native nested-authority regression reproduced the issue and passes. |
| Nested dependencies entered owned creation inventories | Excluded registered nested roots during Git and graph capture/comparison; native regressions cover both modes. |
| Rename into an excluded dependency hid the owned deletion | Shared native Git path projection preserves both boundary endpoints; native inbound, outbound and preexisting-rename cases pass. |
| Excluded locked files were read before filtering | Apply exclusions before hashing tracked, untracked and retained paths; native locked-file and Unicode-path cases pass. |

Final Release suites pass 236 Delivery, 35 Testing and 34 Security tests with no failures/skips. Earlier failures and overlapping successor runs remain retained rather than summed. Candidate 23's installed assemblies match Release; the active installation is unchanged. The original rehearsal remains blocked by its missing authority creation inventory. See the [final-sweep handoff](engineering-defaults-work180-final-sweep.md) for the exact continuation boundary.

## WORK-090 backend closure — 7 October

Separate native read-only Codex review **RUN-20261007132013-C146D11BD0** recommends **ready**, with no findings. It followed successful read-only verification **RUN-20261007131311-06CA3A716E** in a distinct session with matching source/task/authority identities. Claude verification attempt **RUN-20261007131220-D368D6BEFF** failed at the provider's session limit; it is retained as a failure, not review evidence. The configured fallback used the same CIS permission and evidence requirements.

The full native workflow **work090-native-01** passes 38 unit and 40 business cases, 126/128 changed executable lines and all 20 selected mutants; 23 generated mutants remain explicitly ignored. The first adapter-validation bypass exposed a weak assertion because Npgsql also rejects non-UTC timestamps. The strengthened application-message assertion fails all six bypassed cases. Both negative attempts and exact source restoration are retained. Production source is unchanged from WORK-080.

All 24 gates have dispositions: 22 Passed and browser/performance Inapplicable under the approved Foundation scope. Five invalid-receipt controls rejected completion without state changes, then the restored valid receipt completed WORK-090. Repeat completion after strict graph refresh returned unchanged. See the [WORK-090 record](engineering-defaults-work090-backend-rehearsal.md). Controlled service reentrancy does not close database races, workload/restart or recovery requirements; WORK-160 still owns generated catalogue reconciliation.

## WORK-080 consumed-contract closure — 7 October

Separate native read-only Claude review **RUN-20261007114242-444057B684** recommends **ready**, with four observations and no corrective findings. It followed successful read-only verification **RUN-20261007113952-1EE6104200** in a distinct provider session, with matching source/task/authority identities. Workflow **work080-native-01** passes 28 unit and 34 business cases, 126/128 changed executable lines and all 20 selected mutants; 23 other generated mutants remain explicitly ignored. Native field-name and cancellation-exit negatives fail as intended.

| Observation | Disposition |
| --- | --- |
| Wrong-credential CLI failure does not identify the exact provider error | Retain the fixed public message and valid control plus unchanged database readback. A fixture-side authentication-specific assertion remains optional if that distinction becomes required. |
| Generic equality assertions conceal actual stderr in failure output | Preserve current assertions and bounded, redacted retained process evidence. Reassess diagnostics in later cleanup without emitting potentially unredacted provider text. |
| Mutation scope covers Reading.cs, not ReadingCommand | Keep the approved scope explicit. Real process cases and the deliberate cancellation-exit negative cover this contract change; WORK-160/170 can reassess broader mutation scope. |
| Reviewer did not recompute hashes or inspect all alignment reports | Controller checked the 53 staged artifact bindings and implementation hash, inspected current alignment records, and relied on native completion to validate the final receipt. This is separate from the model review. |

All 24 gates are accounted for, with 22 Passed and browser/performance Inapplicable under the approved Foundation scope. Five native receipt controls reject missing, failed, skipped and stale evidence without changing task/plan state. The restored valid receipt completed WORK-080; repeat completion after strict graph refresh returned unchanged. See the [WORK-080 record](engineering-defaults-work080-contract-rehearsal.md). The full phased rehearsal, distributable-example follow-ups and real-project trial readiness remain open.

## WORK-040 security closure — 7 October

Separate native Claude review RUN-20261007103220-063A194BF8 recommends ready for WORK-040 with observations only. Earlier review RUN-20261007101225-BD14286BAD required bounded directory-input errors; the new native regression reproduced exit 1 instead of 2, and the corrected UnauthorizedAccessException filter passes. Review RUN-20261007102409-4A9BED132F confirmed that correction and required explicit historical labels on the remaining WORK-030 assessment section. Those labels were corrected before final workflow work040-native-09 and verification RUN-20261007103021-90237AE65C.

The controller directly bound the schema-negative TRX and verified the authority-held implementation hash. Process-level cancellation remains a WORK-080 consideration, while WORK-160 owns generated catalogue refresh. Native guard-specific assertions remain required. Staged checkboxes did not establish completion: five receipt-negative controls rejected unchanged task/plan state before the restored valid receipt completed WORK-040. Repeat completion after strict graph refresh returned unchanged. See the [task record](engineering-defaults-work040-security-rehearsal.md) for retained failed attempts, exact evidence and remaining CIS/distributable-example follow-ups. No real-project trial readiness is claimed.

## Participant completion closure — 7 October

Native Claude review **RUN-20261007060550-12BD3D52AB** recommends ready for the bounded participant-completion correction. It closes the corrective findings from RUN-20261007044717-9709AADF5C, RUN-20261007051711-4A3F1D2F2D and RUN-20261007054307-D3C228985C: target routing and adoption fail-closed behavior, authority/participant evidence separation, context freshness, exact tool-usage snapshot grammar, overflow-safe bookkeeping and native transition coverage. Current affected-module qualification is 215 Delivery / 186 Agent passes with no failures or skips. Retain earlier failures and their corrections; do not sum repeated runs.

Five nonblocking observations remain recorded: manual wording could make untargeted fallback clearer; one unregistered-target branch lacks a dedicated assertion; legacy negative-savings rows invalidate old evidence conservatively; the reviewer inspected but did not recompute supplied hashes; completion has no transaction lock and still has post-status-write I/O risk. The suggested package sourceManifestSha256 binding was added to rehearsal-20-package-integrity.json. None of these observations grants release readiness or qualification beyond the reviewed scope.

The separate historical WORK-030 review RUN-20261006185841-C09A843B32 was a **Codex** review, not Claude. Its three findings were corrected through native RUN-20261007062203-43D8F614F6 and controller handoff clarifications. Claude review RUN-20261007064435-190AD38DF7 then requested historical source/evidence labels, qualification of a documentation-scope checkbox and a staged completion receipt. Those corrections received final native verification RUN-20261007064932-119E2772F6 and **ready** Claude review RUN-20261007065502-E3F0A7699B, with no corrective findings.

The three closing observations concern routing-copy inventory, the reviewer's inability to re-hash/run commands, and absent later application verification. The inventory was expanded to hash-bind all five run copies, including events. Native CIS then checked the actual artifact digests and rejected missing, failed, skipped and stale evidence before accepting the restored valid receipt. WORK-030 is Complete; a repeat after graph refresh returns unchanged/exit 0. Application source, native suites, test mappings, security/coverage/mutation/business acceptance and later phases remain required. This bounded documentation closure is not Foundation or trial readiness.

These are public-source engineering reviews through the existing provider adapter, not human approval or trial certification. Every review was read-only. Native outputs remain in the isolated worktree's local qualification directory. No real application source or BRD was supplied.

## First bounded review

Run `RUN-20261005233013-816DBF619A` recommended revision. The broad preceding attempt timed out, and resume could not find a non-persistent provider session; neither is recorded as a passing review.

| Finding | Disposition |
| --- | --- |
| Malformed benchmark numeric/date values could throw | Caught as invalid evidence; fractional/oversized counts, operations, exit codes and dates have negative tests. |
| Adoption readers disagreed; deleted policy could bypass execution identity | One bounded JSON/YAML adoption reader serves execution and assessment. Explicit minimal opt-out remains distinct; managed-policy deletion fails closed. |
| Security policy and suite artifact validation were incomplete | Reconciliation retains thresholds/acceptances; completion checks exact suite artifacts, expiry, and native report integrity. |
| Story participants could be missing or silently omitted | Missing/non-owned participants fail safely; non-adoption is explicit. |
| Coverage/mutation summaries were not bound to their own suite/report | Layer and source-path checks require the suite's hashed native report. |
| Qualification incomplete | Correct. Readiness remains separate from implementation fixes. |

## Follow-up review

Run `RUN-20261005235755-3AF8A0117E` confirmed the main fixes and identified further cases.

| Finding | Disposition |
| --- | --- |
| Scanner result producer absent from supplied context | Context omission, not a missing implementation: existing SecurityEvidence.Build produces scanner-result artifacts. Complete source was supplied for the next review; an integration test consumes SecurityService's unchanged manifest. |
| Empty threshold/profile provenance | Empty and unknown severities are rejected; native manifests retain the profile path/digest and completion verifies current bytes. |
| Culture-dependent acceptance dates | Producer and consumer use invariant Gregorian parsing. A th-TH case exercises the consumer. |
| Empty/partial injected story-completion result could pass | Exact participant coverage is enforced; successful results and events retain adoption/completion outcomes. |
| Output-directory names could hide source | Git-aware classification retains tracked/nonignored source; non-Git exclusions are limited to project-adjacent native outputs. A native Git test covers ignored outputs and force-tracked files. |

The first new Git test failed during Windows cleanup of read-only Git objects, after its assertions passed. Cleanup was corrected and the test passed. The participant-removal fixture initially selected an authority-only task; selecting the implementation task exposed and verified the intended registration boundary. Both initial failures are retained.

## Final security follow-up

The complete-source review `RUN-20261006001726-717CEA2407` confirmed the preceding fixes. Three remaining findings were implemented after package `0.3.0-engineering.20261006.1`:

- Unknown, empty, non-finite and out-of-range scanner severities remain unclassified and invalidate the native suite; acceptance reconciliation cannot overwrite that invalid state.
- Completion binds the scanner profile to the repository's actual documentation root, not an arbitrary self-declared file. Missing and redirected paths have negative cases.
- Native reconciliation-to-completion tests cover clean, below-threshold and accepted findings. The accepted case also rejects missing/expired acceptance metadata and modified report bytes.

Their final Release regression and package qualification are tracked in the [progress record](engineering-defaults-progress.md). Review observations about machine-local Git ignore settings and unignored output directories are operational limitations: keep output rules explicit and inspect omitted/generated scope. A fingerprint is not a claim of complete source understanding or a portable cross-machine attestation.

No reviewer has certified the new-project trial ready. The required second executable skill integration and complete packaged phased closing rehearsal remain open until supported by their actual evidence.

## Bounded disposition review

Run `RUN-20261006004125-3A7F55502F` confirmed the severity, profile provenance and native reconciliation fixes. It identified two residual scanner defects: ZAP mapped unexpected risk codes to informational, and malformed Semgrep fields could throw out of reconciliation. ZAP now accepts only its documented 0–3 risk codes; unknown/missing values invalidate evidence. Reconciliation catches malformed adapter shapes and numeric values as invalid evidence. Missing Semgrep severity is unclassified rather than assumed medium.

The reviewer also identified a hidden default for the documentation root. The completion API now requires its caller to supply that root, and the native security integration cases run with both `docs` and `docs/cis`. Production Plan and Story callers already pass their resolved roots. New regression cases cover these dispositions; the progress record identifies their exact validation status. The preceding full Release regression completed after the review context was captured: 1,189 passed, zero failed/skipped across 26 assemblies.

## Final scoped verdict

Run `RUN-20261006005020-17E7936FF3` confirmed the scanner fixes and required-root contract. The reviewer did not run commands; final native verification subsequently passed 29 security, 139 delivery, seven participant and one review-identity test. Its readiness verdict remains blocked by the two missing qualification scenarios.

Remaining observations have these explicit dispositions:

- Production root propagation is verified by source review of `PlanningService` and `StoryEngineeringCompletion`, plus compile-time enforcement of the required parameter. The native integration tests exercise the shared completion boundary for both roots. They do not exercise a scanner manifest through each complete production caller, so that narrower end-to-end wiring case remains a test limitation.
- Positive ZAP informational code 0 and cross-root rejection of a native manifest lack dedicated new cases. Code 0 is explicitly mapped; existing tests cover high and invalid ZAP codes, redirected profile paths and non-default-root native manifests. Retain the reviewer's additional cases as follow-up coverage recommendations rather than claim they ran.
- The suggested non-canonical-root mismatch is not a confirmed defect: `CisRepositoryContextResolver` resolves the full documentation path and returns `Path.GetRelativePath(...).Replace('\\', '/')` as `DocumentationRoot`. Both production callers use that normalized context. This disposition is source-based, not a new runtime test.

No unresolved concrete scanner defect was reported in this final scope. This statement is narrower than full implementation or release approval. No human governance approval or trial-readiness certification was fabricated.

## Corrections to the user-requested review — 6 October 2026

The implementation below addresses the four major and nine minor findings in the [two-pass report](claude-engineering-defaults-review-2026-10-06.md). The final scoped Claude review RUN-20261006080539-BA54C9D174 reports ready with no reproducible source defect; only optional test additions and scope observations remain. The clean Release build and full native regression passed: 1,243 tests across 26 assemblies, zero failures or skips. Subsequent test-only additions passed 74 focused Delivery tests and eight changed-coverage tests. Earlier package version 3 does not contain these fixes; the current isolated package is version 6.

| Finding | Correction and verification scope |
| --- | --- |
| Pass 1 / 001 — policy opt-out | Managed false edits fail closed through the shared policy reader; never-adopted repositories retain an explicit non-adopted state. Negative adoption tests cover deletion and false edits. |
| Pass 1 / 002 — implementation identity | Receipts require a hashed implementation record. Agent records resolve to native run manifests with a matching successful result, task contract and final source digest; reviewer chronology and provider-session separation are enforced. Human authorship is explicit. Protocol negatives cover unknown IDs, missing records, stale inputs/contracts, failed runs, future dates, reused sessions and tampered results. |
| Pass 1 / 003 — unconditional gates | Implementation build and unit gates cannot be marked inapplicable. Coverage requires native evidence; its narrowly defined zero-executable-change case is described below. Other scope-dependent gates still need rationale, evidence and independent assessment. |
| Pass 1 / 004 — stronger thresholds | Adopted policy reads minimumCoverageLines and minimumMutationScore, retaining 95/80 floors. Completion tests exercise higher thresholds on both sides of the boundary. |
| Pass 1 / 005 — artifact diagnostics | Missing or unsafe artifacts produce per-gate errors without hiding later missing gates. |
| Pass 1 / 006 — validator structure | Dedicated implementation, reviewer, test and security validators share JSON and artifact helpers and return specific rejection reasons. |
| Pass 1 / 007 — participant count | Cross-participant freshness failures update the existing participant context. A concurrent-edit test asserts exactly one result per participant. |
| Pass 2 / 001 — implicit adoption | Existing repositories require explicit --adopt-engineering-defaults; --stack alone cannot adopt. Empty repositories receive defaults. Preview is non-mutating and reconciliation preserves existing content. |
| Pass 2 / 002 — SARIF vocabulary | SARIF none and CodeQL recommendation normalize as informational findings; numeric security severity retains precedence. Unknown scanner severities still fail closed. |
| Pass 2 / 003 — dirty-file review edits | Final content identity supplements Git status, detecting edits to already-dirty files. |
| Pass 2 / 004 — scanner exit status | Fresh reports from failed bound workflow steps are parsed and findings retained. The failed execution is not converted to a pass. Native workflow regression executes a fixture scanner that writes a report and exits 1. |
| Pass 2 / 005 — package contamination | Explicit 60-file recipe replaces the working-tree wildcard. Example ignores cover native test, benchmark and editor outputs; package verification injects excluded probes. |
| Pass 2 / 006 — runner discovery | Literal global.json and build-property runner selections are inspected without executing build code. MTP and conditional/update-only references produce qualification warnings and no invented VSTest binding. Production projects remain production. |

The evidence trust boundary is now part of the completion standard and generated ITERATION-006 rule: local hashes detect inconsistency but do not authenticate execution. Reviewer inspection remains mandatory. The omitted example ignore file was supplied in the previous review and is included in the new snapshot. Remaining project-trial qualification gaps are tracked separately; fixing review findings does not claim a completed trial rehearsal.

### Follow-up corrections

- Story implementation uses the task's `-IMPLEMENT` identity and trusted authority repository; the native manifest must target the participant being closed. Both human and native protocol paths pass, with explicit target/task/location negatives and restored-state checks.
- Native AgentService implementation records are consumed by the completion validator in an integration test. Adopted implementation is blocked before provider execution when its task contract is unavailable.
- Coverage inapplicability is derived from executable changed-line evidence: zero measured lines, zero uninstrumented changed files, current source identity, a retained base revision and bound native test/coverage reports. Comments in instrumented files no longer deadlock completion. Missing production instrumentation still blocks. Missing/null count properties are rejected by the shared helper's negative sentinel and explicit tests.
- A separate adoption record preserves the obligation after starter history is removed. Automatic withdrawal or approval of policy exceptions is not introduced. Arbitrary replacement of all local history remains outside unauthenticated consistency checks and is documented as such.
- The explicit recipe has a Git-inventory regression check, and the MTP detector has global runner, both property, MSTest.Sdk, conditional/update and malformed-configuration cases.

The last bounded coverage review's hypothetical absent-field defect was a review-context omission: the shared numeric helper already returned -1. Its source and explicit negatives were supplied for a final check. The reviewer was also supplied the execution-identity exclusion for `.cis/local`; the story test nevertheless now removes its misplaced local copy and asserts exact diagnostic causes.

## Graph and approved-content refresh follow-up

Claude RUN-20261006142534-70DF084B36 closed graph identity and Foundation-specification corrective findings. Ownership ambiguity is explicit and strict validation fails when it exists. The reviewer retained optional coverage limitations: the strict assertion does not isolate the ambiguity diagnostic, same-component partial declarations were confirmed by source only, and the compiler-unavailable path lacks a dedicated test. Native focused-cache and timing files exist separately from that review context. The timing comparison is an observation, not a controlled benchmark.

The real rehearsal exposed approved technical-content regeneration during provenance refresh. RUN-20261006144007-164621713F confirmed the narrow preservation correction but requested native passing evidence and clearer test coverage. The complete fixed-code module suite passed 30 cases, and the final three-case focused run passed with explicit narrative checks. A requested empty-workspace/stale-participant matrix row has no applicable baseline: its failed fixture attempt is retained and the final empty case explicitly verifies that absence. The closure reviewer has the code and native logs/TRX for this disposition.

The following remain separate observations, not completed fixes: draft-path metadata replacement and regeneration can discard managed-block customizations after material change; classified technical surfaces can diverge while repository provenance carries forward. The current correction preserves approved content only when semantic authority remains valid. It does not certify every customization or classification-change workflow.

Closure review RUN-20261006145223-A0DBA3DDFC found no remaining corrective refresh findings after the native 30-case suite and three overlapping final cases. The actual candidate-11 recovery/refresh then preserved the retained approved document byte-for-byte and restored Active/current architecture/UI. Native evidence, rather than the reviewer, establishes that runtime result. Negative evidence records the pre-correction source behavior; the failure's first differing metadata field alone does not establish every aspect of narrative replacement, which is separately retained in the real before/after diff.

Schema-4 starter review RUN-20261006150648-8FED36A664 also returned ready without corrective findings. The code recognizes exactly schemas 2/3/4 and retains both marker requirements. All 239 Repository tests pass; candidate-12 rehearsal preview removes the false conflict. Optional preexisting limitations remain: shared schema ownership, negative tests for unsupported/malformed documents, and stricter nested front-matter/marker parsing. Actual completion still rejects unapplied adoption, so neither the reviewer nor the package check certifies task closure or release readiness.

## Reconciliation proposal, 6 October 2026

The approved scaffold commit and adoption reconciliation are complete. Their native checks pass and repeat reconciliation is unchanged. Technical-intent authority remains Stale due to repository/standard baseline drift; this is not a completed task.

Automatic approval review rejected canonical `technical-intent init` because its material-change path could replace reviewed prose and metadata without an atomic preservation guard. The command did not execute. An unapplied Draft proposal changes only baseline/provenance and the standard-document count, with approval fields unset. A mechanical comparison confirms all other body text is identical and the original canonical file is unchanged. Validation and explicit approval remain outstanding.

Claude proposal review RUN-20261006165420-C3065C6DA4 failed at its session limit (reported reset 21:00 Europe/Malta); it produced no review. Codex review RUN-20261006165647-2A7587730E returned blocked because the no-command prompt prevented file reads; that report is evidence-access failure, not a code finding or passing review. A fresh read-only Codex run allows bounded local text reads and preserves the same proposal/source scope. Its result will be retained separately.

Proposal follow-up RUN-20261006165845-3842B446C8 found no content/digest defect but kept provenance assurance blocked. RUN-20261006170353-B84D4D75B5 timed out after confirming the declared diff and six normalized standard digests. Retain the incomplete review; do not relabel it ready. The subsequent read-only Git binding checked all 111 exact preview files through path-specific Git filters against the committed tree and found 111 matches. Canonical technical content remains unchanged. This is concrete local provenance evidence, not an authenticated attestation or agent implementation record. Approval of the separate guarded-refresh proposal and independent provenance closure are still pending.

## Guarded refresh approved; architecture reconciliation qualification

The user approved the exact technical-baseline proposal. Installation checked original and proposal hashes, retained an atomic backup, passed native validation, and recorded approval through CIS. Technical intent is Active/current. Evidence is retained in `phased-adoption-proposal/approved-install-receipt.json`, `phased-adoption-installed-validation.json`, `phased-adoption-technical-approved.json` and `phased-adoption-approved-technical-intent-status.json`. The earlier automatic-review block is resolved for this exact operation; the broad regeneration path was not retried. All sixteen choices, topology, workload limits and deferrals remain unchanged.

Architecture and UI correctly became stale against the new technical version. Existing explicit-model reconciliation requires implementation inference, which does not fit a greenfield participant containing scaffolding only. A bounded `solution-design reconcile` command is under qualification: reviewed raw file hashes and the technical version guard a deliberate actor/reason assertion; narrative and diagrams are retained; both approvals are cleared; no implementation-inference provenance is invented. Native SolutionDesign tests pass 71 cases, zero failures/skips, including input rejection and exact restoration after source approval becomes stale during validation. Independent review and downstream qualification are pending. Do not apply this new command to the rehearsal before review. WORK-030 remains InProgress; no Foundation implementation or task completion is claimed. Candidate 12 remains the last packaged baseline; the unbundled change is not trial-ready.

## Guarded architecture reconciliation: independent review corrections

Codex review RUN-20261006172454-0F4D3C2C91 returned blocked. It identified whole-document metadata replacement (TASK-REV-002), incomplete rollback after a restoration failure (003), and format validation after mutation (004). All three were corrected before use: canonical front matter and its cis mapping are the only metadata targets; a dedicated transaction retains original bytes and a recovery inventory, attempts every restoration, and reports partial restoration explicitly; reconcile rejects unsupported formats before invoking the service. Added native tests exercise matching YAML/comment keys, late-write failure, a restoration failure followed by successful later restoration, and non-mutation on invalid format.

TASK-REV-001 requested material source dependencies and native verification evidence. The closure review now has the complete production/test module, supporting contracts and package declarations, the final 75-case SolutionDesign TRX and the earlier 31-case Definition TRX clearly labelled historical. TASK-REV-005 was addressed by stating the JSON status command used to obtain technicalIntentVersion. Final downstream tests and independent closure are pending. No crash-atomic bundle guarantee, implementation-manifest identity or Foundation completion claim is made.

## Candidate 14: reviewed reconciliation and Foundation execution

Independent Codex closure RUN-20261006174226-95A0E978EB is ready with no corrective findings. It closes the missing-dependency and source-version findings from RUN-20261006173313-7A5E5C44D6; that earlier review had already closed metadata mutation, incomplete restoration, invalid-format mutation and instruction clarity. Final native suites pass 79 SolutionDesign and 31 Definition cases, zero failures/skips. Nine ArchitectureAuthoring cases passed before the last source-hash correction and remain historical. No whole-bundle crash atomicity or multi-writer locking guarantee is claimed.

Candidate 14 package SHA-256 is `B6D5CE936E55030804B6C4638394E74940EB8815DB02C43D4EE2319C584E174D`. All 62 packaged/exported recipe files match source; SolutionDesign, Graph, TechnicalIntent and Repository DLLs match the build, with no generated-output leaks. Candidate 13 was superseded before rehearsal use after its initial local tool-install command encountered NuGet source mapping. Candidate 14 uses a dedicated local-only package configuration. Neither candidate replaces an active installation.

The native guarded reconciliation succeeded in the disposable authority. Both architecture document bodies and all three diagram files remained unchanged; the existing explicit synthetic-design approval was carried forward through CIS. Native UI reconciliation changed source baseline references only and retained the approved no-visual-UI scope. Technical intent, architecture and UI are Active/current. Strict workspace and root graph validation passed. The authority's manifest-only adoption refresh repeats with zero changes/collisions; the application preview is unchanged.

The first clean missing-evidence completion control now rejects WORK-030 with exit 2/applied false specifically because its closing receipt is missing. This is actual missing-evidence rejection, unlike earlier attempts blocked at adoption. It does not establish stale/failed/skipped rejection or successful closure. The first real WORK-030 Codex implementation run has started in an isolated application worktree through candidate 14; its result is pending. No Foundation task is complete, and the real-project trial remains **Not ready**.

## Participant context defect found during native execution

Candidate-14 implementation run `RUN-20261006174852-C7A89EC3D6` returned a valid provider result but reported WORK-030 blocked because authority feature/change files were absent from its isolated application checkout. It changed no files and ran no task verification. Provider transport success is not implementation success. WORK-030 remains InProgress and no completion evidence has been fabricated.

A bounded correction is in progress in `AgentService.AuthorityContext.cs`: participant envelopes retain complete, size-limited UTF-8 authority snapshots with raw hashes, explicitly distinguish authority-relative references from participant files, and reject source drift during execution or before resume. Product requirements, technical direction, architecture, UI and backlog join the existing task/feature context where present. Participant files and authority permissions are not broadened by copying documents into the target. Streaming hashing avoids loading an unbounded source file merely to calculate its digest. Native tests and independent review are pending; this correction is not yet packaged or used for a second Foundation run. Candidate 14 remains the last independently reviewed package.

Review RUN-20261006180435-3061335D86 found missing transport source (001), unbound canonical-document selection (002), safety/size checks after preliminary hashing (003), and missing boundary/resume tests (004). Corrections bind document-selection state, including its absence; safely read bounded UTF-8 artifacts before deriving participant scope hashes; convert preparation/read failures to structured diagnostics; and check the same source/selection state at completion and resume. Tests now cover selection changes, exact file/aggregate limits, aggregate overflow, invalid UTF-8, locked input, unchanged resume, and complete large payload delivery through the native stdin runner. Provider source will accompany closure review. The Windows file-symlink fixture failed because this account lacks the required privilege; it was replaced by a directory-junction fixture. The latest full Agent run therefore records 170 passed and one environment/setup failure, not a fully passing suite. The final focused rerun is pending. Newly generated architecture guidance also now includes guarded reconciliation and its separate approval boundary; the existing bootstrap test passes.

Participant-context verification now includes 170 passing cases in the latest full Agent run (one Windows symlink-privilege setup failure), 13 passing focused boundary/continuation cases (one junction path-setup failure), and the corrected junction case passing independently. The reports overlap; no clean 171/172-case suite is claimed. The junction rejects the linked source with its target locked, so the safety failure precedes target reads. The prior 161-case clean suite is historical. The native bootstrap guidance case passes. Final source review is running with both real provider implementations and all relevant helpers included; its verdict is pending. Candidate 15 is superseded before rehearsal use; candidate 16 includes the closure corrections and packaged architecture guidance.

## Participant eligibility and input-delivery review corrections

Independent review `RUN-20261006182655-D9B2849B49` requested two further corrections before the Foundation retry. Eligibility read the approved plan and feature before applying the snapshot safeguards, and a provider that stopped reading stdin could block prompt delivery beyond timeout or cancellation.

The source now bounds and validates eligibility reads before parsing or hashing, returns structured read failures, and checks participant eligibility again against the exact bound snapshots. A shared input-delivery helper applies cancellation to writes and flushes in both provider transports and terminates a stalled child process tree. Eight focused native xUnit cases pass, including a linked feature with a locked target, oversized and locked inputs, and non-reading children under both timeout and explicit cancellation. The full Agent suite and independent closure review are pending. Candidate 16 has not been adopted or used for Foundation implementation. WORK-030 remains InProgress, and the real-project trial remains **Not ready**.

## Candidate 17: participant context closure and Foundation retry

Independent closure `RUN-20261006184355-FA85037A97` is ready with no remaining corrective findings in the bounded participant-context and transport scope. The completed native Agent suite passes **180/180**, with zero failures/skips (`participant-context-reviewed-fixes-full.trx`). The review saw the eight focused regression results while the full suite was still running; the completed full-suite result is separate subsequent verification. Earlier failed attempts remain historical evidence.

Candidate 17 SHA-256 is `44E4C175D4E34FB6A69BBF5DF46A90D375AC7F8FD8C19C6CDECE0F85B06B7A7F`. All 62 bundled/exported recipe files and five affected/dependent assemblies match source/build bytes; no example output leaks were found. Both synthetic repositories received exactly the previewed reconciliation skill, instruction and starter-manifest updates. Repeat adoption previews report zero creates, updates or collisions. Their graphs pass strict validation; technical intent, architecture and UI remain Active/current. No canonical reapproval was needed.

The candidate-17 WORK-030 implementation retry has started in an isolated application worktree. Its result is pending. No Foundation task is complete, and the real-project trial remains **Not ready**. Root documentation, standards and skill checks pass, and the deterministic local skill audit is clean; a final graph refresh will follow the current source/document edits.

## Candidate 17 actual task result and remaining closure work

WORK-030 implementation `RUN-20261006184944-60D9AF1F75` produced six participant documentation files using the supplied authority snapshots. Exact baseline checks and hashes protected their transfer; native strict documentation/reference checks subsequently pass. Separate review `RUN-20261006185841-C09A843B32` used the same final participant source identity and returned three findings: missing all-standard/skill alignment and gate inventory; an unfilled participant technical-intent scaffold; and handoff wording that omits the later controller validation passes. WORK-030 remains InProgress.

The actual participant implementation/review identity also differs from the authority identity used by direct task completion. This is a confirmed source-routing gap, not proof from a fabricated completion receipt. Safe refresh restored Active/current BRD and technical intent but left the backlog Review Required. Its retained diff changes the older technical-version reference and clears approval metadata; item rows and prose are unchanged. No approval has been restored manually.

The [participant closure follow-up](engineering-defaults-participant-closure-follow-up.md) records the correction order, native acceptance cases and exact evidence. Candidate 17's 180 passing Agent cases and closed bounded source review remain valid. They do not close these newly observed workflow findings or qualify the real-project trial.

## WORK-160 traceability and catalogue reconciliation

The documentation authority incorrectly required its own executable suite profile while tracing participant results. Candidate 22 validates the registered manifest producers' profiles and rejects mismatched owner identities. The old positive regression fails; the final Testing-module run passes 35 cases. Native authority tracing initially passed 43 TC-linked executions (68 after the precision corrections), while the old unmapped run remains rejected. The current implementation verification inspected the hash-bound product source and participant closure evidence; final review is recorded below.

Catalogue-only re-import removed customized task handoffs. Exact before snapshots were restored while retaining native catalogue/CSV outputs; approved plan and feature bytes stayed unchanged. Generic preservation is not fixed by this workaround and remains an explicit product follow-up. See the WORK-160 rehearsal record.

### WORK-160 precision findings

Review RUN-20261007141649-B1E5D30676 requested revision for TASK-REV-001 (timestamp truncation) and TASK-REV-002 (quantity rounding/underflow). Eight native regressions reproduced both defects before correction. The input boundary now retains native JSON tokens for focused precision checks before exposing readings. Eight unit regressions, eight CLI negatives and nine compatible-form cases cover the correction. Full work160-native-04 passes 103 tests and 98.20% changed executable coverage. Fresh source-bound verification and independent closure review remain pending; earlier passes remain historical evidence.

### WORK-160 completion evidence ordering

Review RUN-20261007145145-58400E7831 supports the precision corrections and identifies no further application defect, but requests current completion-rejection evidence before its verdict. It also flags stale CLI-gate wording claiming unchanged production bytes. The rationale now identifies ReadingInput and both new helpers while preserving the command/output-contract distinction.

Six native pre-review controls retain the Missing review gate and its three baseline provenance diagnostics. Each targeted missing/failed/skipped/stale/unresolved condition produces its own exact additional rejection (missing receipt returns its direct rejection), exit 2 and unchanged task/plan bytes. Exact receipts are restored after each attempt. This supplies current WORK-160 evidence without inventing a successful review. The controller will repeat controls with the fully bound receipt after a ready verdict, then attempt valid completion. Evidence-only closure review is pending; code and task identities are unchanged.

### WORK-160 failure diagnostics

Review RUN-20261007150408-B468284140 supports the precision correction but finds missing partial CLI diagnostics on timeout/cancellation and reader failure. Capture, lifecycle and persistence now have focused helpers with independent cleanup deadlines, bounded redaction and original-failure propagation. Four native harness regressions pass; disabling failure retention produces three failures before exact source restoration. Analyzer feedback was fixed without suppressions. Full current qualification, fresh implementation verification, independent review and completion controls remain required.

### WORK-160 current review and dispositions

Current workflow work160-native-05 passes 107 distinct tests (46 unit, 61 business), 68 TC-linked executions, 164/167 changed executable lines and all 20 selected Reading.cs mutants. Native Codex verification RUN-20261007153941-9E8BE7C813 succeeded read-only. Independent Claude review RUN-20261007155259-0DDC44AD99 recommends ready with three observations and no corrective findings. Both retained identical current inputs; neither reran the controller-owned native suites. The graph catalogue raw-hash discrepancy is resolved by reproducing the product's normalization algorithm and obtaining the exact recorded hash.

| Observation | Controller disposition |
|---|---|
| TASK-REV-001: no explicit timestamp offset/designator | Unconfirmed inherited contract ambiguity for WORK-170 and subsequent contract assessment. No runtime behavior or coverage is claimed. The current approved enumerated UTC/offset criteria are unchanged; no criterion is waived. Resolve this before extending or publishing the reusable input contract. |
| TASK-REV-002: precision-helper mutation | Keep the approved Reading.cs scope explicit: 20 killed, 23 ignored. No helper mutation coverage or independent fault detection of redundant guards is claimed. No WORK-160 change is required. |
| TASK-REV-003: closure protocol | Bind the original ready review and repeat all six rejection controls before native completion, graph refresh and repeat completion. Actual transition evidence is recorded in the WORK-160 report. |

Earlier failed and superseded provider runs remain retained. Current pre-review-completion-controls-02 passed all six controls without changing task/plan bytes. The generic task-customization preservation fix, distributable example ports, full phased rehearsal and second CLI-skill qualification remain open.
## WORK-170 assurance findings

The current Foundation native run passes 107 tests and the four required diagnostic tests separately. The initial report binding was rejected and corrected before fresh work170-native-02 execution. Whole-change verification remains invalid because of missing authority baseline/snapshot and final-delivery evidence routing; these are retained WORK-180 follow-ups, not passes.

Native assurance RUN-20261007181045-FE4F7D7619 attempt 1 found no corrective Foundation implementation defect, but requested current task rejection controls. Its InvalidEvidence status and structured-completion diagnostic remain retained. Six pre-assurance controls now supply those actual rejections without state changes or invented agent bindings. The resumed assurance must succeed before its provenance can be bound, and a separate reviewer is still required. See the [WORK-170 record](engineering-defaults-work170-assurance-rehearsal.md).

WORK-170 assurance attempt 2 succeeded with unchanged inputs. Separate Codex review RUN-20261007183603-8D82E455A7 recommends ready with four observations and no corrective findings. Claude's intervening quota failure remains retained without a verdict. The observations preserve existing ownership: contract syntax to the Foundation contract owner, coverage/mutation limits to the harness maintainer, baseline/snapshot and workspace evidence routing to WORK-180, and generic customization preservation/example ports to the CIS maintainer. None establishes full trial or release readiness. Native task closure evidence is recorded in the WORK-170 report.

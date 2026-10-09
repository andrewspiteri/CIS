# WORK-160 targeted and regression verification rehearsal

Historical qualification record: candidate identities, commands and unresolved states below describe that checkpoint. For the completed Windows .NET milestone and current trial instructions, use the [readiness record](engineering-defaults-trial-readiness.md) and [trial guide](engineering-defaults-dotnet-trial-guide.md). Earlier receipts and findings remain retained.

WORK-160 is **Complete** through native CIS after correcting precision and failure-diagnostics findings. Current verification and independent Claude review support closure; earlier source-bound records remain retained as history. The approved feature, dependencies, storage schema and public signatures are unchanged.

Eleven native xUnit methods carry TC-FEAT-FR-001-001-001 in DisplayName. Native catalogue regeneration records one Automated case, zero Pending and eleven real method references. Its CSV SHA-256 is fefa9cbc876ad9c829fee504e0c1046d2716c8a1feefb62c2c6556cf1ebc5dda. The Foundation reference maps individual criteria; aggregate identity recognition does not prove every criterion.

Current workflow work160-native-05 passed all 15 steps: 46 unit and 61 business cases, zero failures/skips, 68 TC-linked executions and 164/167 changed executable lines covered (98.20359281437126%). Coverage includes both new helpers with zero uninstrumented changed files. Reading.cs mutation retains 20 selected mutants killed and 23 explicitly ignored; mutation of the new CLI helpers is not claimed. Required security scans reconcile passed; Trivy uses HIGH/CRITICAL selection and Semgrep remains supplementary due to partial C# parsing. Report aliases do not add distinct cases.

Operational acceptance uses four existing diagnostic-listener checks, four new failure-capture cases and native CLI help smoke. This qualifies bounded Foundation observability; browser and a new performance budget remain inapplicable. Later workloads, deployment, restart and crash recovery remain separate.

## Independent review found two missing regressions

Initial verification RUN-20261007140659-59EE1CF22E reported no blocker. Separate review RUN-20261007141649-B1E5D30676 recommended revise. TASK-REV-001 showed timestamp fractions beyond native tick precision being truncated before Core validation. TASK-REV-002 showed quantity rounding/underflow into an apparently valid decimal. These are exact individual-input defects, distinct from the excluded aggregate-total limitation.

ReadingInput now retains the native JSON document while using the same serializer and required-constructor rules. ReadingJsonPrecision checks supplied timestamp fractions; ExactQuantity checks significant coefficient digits and exponent without rounding. They validate the whole input before readings reach application/storage code. Core validation still governs typed values. No replacement serializer, test runner or shared framework is introduced.

All eight new native unit regressions failed before correction and pass afterward. Eight real CLI negatives prove rejection and unchanged persistence after a successful control. Nine CLI positives preserve supported numeric strings, exponent forms, exact quantity boundaries, trailing-zero timestamp fractions and zero forms through persisted values. The initial expanded test-data method hit the existing complexity rule; PrecisionInputCases now owns focused data, preserving the threshold. The corrected focused business run passes 38 selected cases.

The formatter failure and superseded passing runs remain retained. Run 02 became stale after starter reconciliation. Run 03 passed 78 tests but missed the subsequently reproduced defects. Run 04 follows the precision correction, catalogue refresh and reconciliation. Earlier execution and provider verdicts cannot close the corrected task; fresh verification and independent review are required.

## CIS trace correction

The documentation authority owns the catalogue; participant repositories own native test profiles. Previous tracing demanded an executable profile in the authority even when only the participant ran tests. The fix validates the profiles of registered repositories that produced the selected run's manifests. It also rejects a manifest whose repository identity differs from its registered location. Standalone behavior and missing/failing/skipped participant evidence remain fail closed.

A positive workspace regression fails against the old routing. The final native Testing-module suite passes 35 cases, including absent profile, failed/skipped execution and known/unknown mismatched manifest owners. Candidate 22's installed Testing assembly matches the Release build. Its package SHA-256 is f8f549f9466af3ec07cefbbcd22e50e442c60f5caf78aa1e382198146030aac9. Candidate 21 was built but its local installation failed on package-source mapping; candidate 22 installs using a task-local NuGet configuration. Global NuGet settings and the active CIS installation remain untouched.

The original trace passed with 43 distinct execution names; current corrected tracing passes with 68. The corrected candidate still rejects the old WORK-090 run with exit 4 because its native results contain no stable TC identity. The erroneous authority-profile diagnostic is gone. A passed aggregate identity alone does not prove every criterion; the mapped cases, full suite and independent review provide the additional evidence.

## Reconciliation follow-up

Catalogue-only feature re-import removed customized task handoffs despite unchanged approved feature bytes. Exact task snapshots were restored, retaining the native generated catalogue/CSV. The approved plan bytes and feature hash are unchanged. Before/after snapshots remain under WORK-160 closure evidence. This is an explicit workaround; reusable preservation of customized task content remains a product follow-up before claiming reliable generic reconciliation.

Strict graph, documentation, references, standards, suite profiles and skill validation pass. Deterministic skill audit and conformance inventory also pass; loading enforcement mappings does not itself prove evaluator compliance. Both doctors pass with advisory warnings retained, and final reconciliation previews are clean. Hash-bound source/test/report copies extend independent review to the CIS routing correction.

## Corrected verification

Native read-only verification RUN-20261007144326-96D28286EA succeeded with no additional blocking implementation defect and unchanged input/output identity. It inspected corrected source and retained reports without rerunning the native suites. A source-manifest comparison confirms the only production delta from WORK-090 is ReadingInput plus new ReadingJsonPrecision and ExactQuantity. Review RUN-20261007145145-58400E7831 supported the code correction but requested current completion-rejection evidence before its final verdict and corrected a stale CLI-source statement in the receipt. Six pre-review controls now reject missing receipt/gate, failed/skipped gate, stale input and unresolved finding with unchanged task/plan bytes. Expected Missing-review diagnostics remain explicit throughout; no ready review was invented. The CLI rationale now identifies the actual production delta. That evidence-only review was superseded by the diagnostics correction below; original source-bound records remain retained.

## Failure-time diagnostics correction

Review RUN-20261007150408-B468284140 supports the precision fixes but identifies TEST-013 failure-time capture loss. The old CLI helper threw after timeout before retention; its cancelled output readers could lose buffered text. CliProcess now configures the actual CLI and delegates to ProcessCapture for lifecycle, BoundedProcessOutput for incremental bounded prefixes, and CliProcessEvidence for independently bounded redacted persistence. Original failures propagate after child cleanup and retention. A ProcessDiagnostic record groups metadata; xUnit still owns assertions, discovery and results.

Four native regressions cover timeout, explicit cancellation, output overflow and reader failure. Controlled native PowerShell children verify retained stdout/stderr, redaction including a secret cut at the capture limit, capture bounds, original failure and child exit. Disabling failure-time retention produces three native failures and one pass; exact source was restored. Existing analyzer rules caught cancellation signatures, parameter count and a nested ternary in the initial draft; refactoring satisfied them without relaxed rules. The four corrected focused cases pass. Source/standard reconciliation was followed by full run work160-native-05, which passes all 107 cases; earlier work160-native-04 results and controls remain historical.

## Current source verification and controls

Native Codex implementation verification RUN-20261007153941-9E8BE7C813 succeeded read-only with unchanged input/output identity after work160-native-05. It independently counted the 107 tests, projected changed-line coverage, checked source/PDB and artifact hashes, and inspected the routing correction. It did not rerun native suites. Its catalogue-hash query is resolved: GraphBuilder normalizes catalogues before hashing, and reproducing that algorithm exactly matches the stored hash. Raw-byte comparison alone was inappropriate for that input.

All six current-source pre-review rejection controls passed with unchanged task/plan bytes, retaining the expected Missing-review diagnostics. Separate native Claude review RUN-20261007155259-0DDC44AD99 then recommended ready with three observations and no corrective findings. Source/task/authority identities match the implementation run, provider sessions differ and review follows implementation. Neither provider reran the controller-owned native suites.

The observations are explicitly dispositioned: timestamp strings without a designator/offset remain an unconfirmed inherited contract ambiguity for WORK-170; mutation remains limited to Reading.cs; closure requires actual controller execution. No new input behavior or risk acceptance was approved. See the [review dispositions](engineering-defaults-review-dispositions.md).

The final receipt records 23 Passed and two Inapplicable gates. Operational evidence is an additional explicit gate beyond the native 24-gate inventory. All six controls repeated against the fully bound receipt reject exactly the targeted missing receipt/gate, Failed/Skipped gate, stale identity or unresolved finding, preserving task/plan bytes. Valid transition returned task-transitioned, exit 0, applied true. Workspace graph rebuild and strict validation passed; repeating Complete returned unchanged, exit 0, applied false. Original results are retained under closure/transition-complete.json, closure/post-completion-graph-validate.json, closure/transition-repeat.json and ../completion-controls/summary.json.

## Evidence and remaining work

Current participant evidence is under .cis/local/workflows/work160-native-05, .cis/local/testing/runs/work160-native-05, .cis/local/security/runs/work160-native-05 and .cis/local/qualification/WORK-160. The authority receipt is docs/cis/changes/CIS-0001/verification/WORK-160/application.json. Product regressions, precision failing-before/passing-after reports and failed build/packaging attempts are retained under root .cis/local/qualification/WORK-160-tooling.

Next is WORK-170 independent assurance. Task review does not replace that broader assurance. The phased rehearsal, customization-preservation fix, distributable-example ports (including precision and diagnostic-capture fixes) and second CLI-skill qualification remain open. The active installation is untouched and the real-project trial is not ready.

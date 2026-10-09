# .NET trial readiness — candidate 34

**Ready for a new .NET/C# project trial on Windows.** Independent source-aware readiness review and a separate unfamiliar-reader review are complete, with no unresolved corrective findings.
This record supersedes the open-readiness statements in earlier candidate checkpoints; those receipts remain historical.

Candidate: `0.3.0-engineering.20261009.34`.
Package SHA-256: `f7933ac528ece12d04218b9bcfd2351af9792f0099491ea9c90f042a90aded89`.
The installed candidate is `.cis/local/engineering-tool-rehearsal-34/cis.exe` in this worktree.
The [Windows trial guide](engineering-defaults-dotnet-trial-guide.md) gives the next-project workflow.
The later [documentation reconciliation](engineering-defaults-documentation-reconciliation.md) updates source guidance and records reference-discovery limits without replacing this package qualification.

## Final qualification

The retained `.cis/local/qualification/trial-readiness/qualification-summary.json` binds the package,
source revision plus dirty-file inventory, four native TRX reports and the actual provider transcripts.
`local-timestamps/candidate34-integrity.json` verifies 42 packaged CIS assemblies, 92 example files and
five build/test/install assembly bindings. All 92 installed example files match source byte for byte.
The candidate is deliberately isolated; no active installation, branch merge or publication has changed.

| Check | Outcome and evidence |
| --- | --- |
| Per-attempt command permissions | Optional native command rules, provider capability validation, restrictive rule syntax, actor/attempt journal and fresh authorization on resume. Read-only and unsupported providers reject the option. Source and unfamiliar-reader reviews are ready. |
| Native regressions | 249 Agent tests passed before the final repeated-option parser correction; all 20 focused permission/CLI cases passed after that correction. All 17 Host tests passed. Counts overlap and are not summed. |
| Actual second-provider skill | Installed candidate Claude run `RUN-20261009084441-352C85E3C7` read the portable skill, built Release, ran help and executed the boundary query. Native tool-result records confirm all three commands, zero permission denials and no source changes. The expected deliberate defect was observed: end value 10 returned `contains:true` for a half-open interval. Caller duplication was explicitly source-derived, not claimed executed. |
| Permission negative control | `RUN-20261009084703-C50066206C` attempted Debug while only Release had a rule. The native tool denied it; CIS returned Failed / permission-required, exit 4. This demonstrates that different arguments are not silently authorized; it does not establish full shell confinement. |
| Clean declared/growth bootstrap | Installed candidate initialized a declared C# repository and a separate initially empty repository that later gained C# source. Applicable guidance/examples appeared; both graph builds passed and repeat initialization reported no creates, updates, collisions or errors. |
| Native verification from installed example | All 57 unit cases passed from the fresh automatic local export. The earlier sandbox NuGet-config access failure is retained; execution under the user's account supplied the successful native receipt. |
| Completion smoke | The already accepted Foundation remains closed. Candidate 34's first whole-change validation correctly detected stale acceptance/closure bookkeeping. Native diff refresh changed only authority events, proposal and verification records; original baseline, application files and scope were unchanged. Final compare/validate passed with zero findings; repeated close returned unchanged. No approval was renewed or invented. |
| Broader predecessor evidence | Candidate 32 affected Delivery/Repository/Testing/Agent and native web checks, candidate 33's 57 unit/64 business and exported local-time replay, and the seven-task Foundation execution/review/acceptance remain identified by their original receipts. They are not relabelled as new candidate-34 executions. |

Final strict documentation, standards and skill validation pass; deterministic skill audit is clean.
Root initialization repeats without changes or collisions. Doctor reports warnings with no errors; optional
missing state and conservative runner qualification remain visible. Closing graph build and strict validation
are retained beside these reports. Formatting verification for the new permission files passes.

The final environment is Windows `10.0.26200.0`, .NET SDK `10.0.401`, PowerShell `7.6.5`.
Native framework, analyzer, browser, database, benchmark and scanner versions/prerequisites are pinned in the
[example manifests and README](../examples/dotnet-engineering/README.md). Claude qualification used the installed
2.1.295 Windows CLI. Use short checkout paths. Restore, Docker availability and browser installation remain
explicit prerequisites for applicable checks, not assumed passes.

## Implementation-plan disposition

Every row is complete for the bounded Windows .NET milestone, subject to the limitations below.

| Tasks | Implemented mechanism and qualification route |
| --- | --- |
| IP-00, IP-01 | Existing native module owners, adopted versioned engineering policy, required inventory and six evidence states; native regression and final Foundation gates. |
| IP-01a | Provenance-preserving raw intake, unresolved decision handling and synthetic phased rehearsal; [BRD checkpoint](engineering-defaults-brd-layout.md) and actual question/review receipts. |
| IP-02, IP-03, IP-04 | Bounded native framework discovery, ownership-aware all-standard reconciliation and required gates independent of configured commands; [automatic examples](engineering-defaults-automatic-examples.md), candidate-32 growth/preservation fixes, candidate-34 fresh declared/growth receipts. |
| IP-05, IP-06, IP-07 | Native xUnit/TRX/Coverlet/Stryker recipe; test-only logs/metrics/traces and bounded crash/timeout evidence; native benchmark/budget checks and incompatible/missing controls. [Progress qualification](engineering-defaults-progress.md), [WORK-160](engineering-defaults-work160-verification-rehearsal.md), [WORK-170](engineering-defaults-work170-assurance-rehearsal.md). |
| IP-08 | Sonar/SDK metrics/formatting, partial-type coupling and architecture negatives; cohesive Core/Persistence/CLI example and migration counterexamples. Same native recipe and review evidence. |
| IP-09, IP-10, IP-11, IP-12 | PostgreSQL integration, API compatibility, denied browser/API behavior, native security evidence and 500-reading complete readback/replay plus orderly process restart. [WORK-040](engineering-defaults-work040-security-rehearsal.md), [WORK-080](engineering-defaults-work080-contract-rehearsal.md), [remaining acceptance](engineering-defaults-remaining-acceptance.md). |
| IP-13, IP-14 | Thin native CLI and canonical portable skill shared by supported tool integrations. Prior actual Codex execution plus the candidate-34 actual Claude execution above close the remaining executable-skill gap. |
| IP-15, IP-16 | Interchangeable independent reviewer adapters, source/task/result identity, callers and explicit context omissions; actual seeded reviews and stale/malformed/unavailable native controls. [Review dispositions](engineering-defaults-review-dispositions.md) and final permission-flow source review. |
| IP-17, IP-18 | Closing graph/alignment/required-gate receipt and assigned-reviewer double-check; native task/story completion rejects missing, stale, skipped, failed and unresolved evidence. Seven completed Foundation tasks retain distinct implementation/review receipts. |
| IP-19, IP-20 | Existing-policy/alternative-framework preservation and native synthetic migration with deliberate defect/restored pass; recorded predecessor regression, preservation fixes and final bootstrap smoke. |
| IP-21 | Full authorized Foundation task chain, original baseline, independent reviews, whole-change checks and separate human acceptance/closure. Later synthetic product features remain outside this CIS-readiness scope. |
| IP-22, IP-23 | Installed candidate binding, fresh bootstrap/growth and native verification, real provider execution, revalidated closed Foundation, final evidence mapping and current Windows trial guide. |

## Qualification-case disposition

| Case | Outcome and evidence |
| --- | --- |
| EQ-01 | Empty-to-C# growth detected and reconciled; final `growth-*.json` receipts. |
| EQ-02 | Declared C# stack installs applicable defaults before source exists; final declared bootstrap and native export receipts. Framework binding remains unverified until executed. |
| EQ-03 | Human customizations/stronger rules retained; native ownership/preservation/drift fixtures and idempotent initialization. |
| EQ-04 | Native unit/coverage/mutation, integration/business, API compatibility, browser and security layers; predecessor known-failure/restored controls and final native export checks. Bounded mutation scope and unavailable layers remain disclosed. |
| EQ-05 | Lint, partial-type coupling and architecture violations detected; restored native checks and responsibility examples. |
| EQ-06 | Test instrumentation attribution/isolation plus timeout/crash partial evidence; disabled/missing collection is not a pass. |
| EQ-07 | Native representative business/performance evidence; missing/undersized/incompatible/regression controls and exact 500-reading results. |
| EQ-08 | Actual Codex and Claude portable-skill execution; final package transcript confirms real build/help/query, plus denied-command negative. |
| EQ-09 | Actual interchangeable seeded-defect reviews; deterministic unavailable/stale/malformed-provider regressions. |
| EQ-10 | Source-backed caller/context inspection and omitted/stale evidence controls; no whole-repository completeness claim. |
| EQ-11 | Native completion rejects absent required gates despite passing configured commands; all six gate states qualified. |
| EQ-12 | Corrected findings, preserved profile/authority customizations, fresh graph/checks/reviews; seven-task Foundation completion and final bookkeeping refresh. |
| EQ-13 | Requirement/test mapping and blocking-findings completion controls; Foundation TC automation and retained native receipts. |
| EQ-14 | Evidenced docs-only/unchanged paths and scoped inapplicability; native regressions preserve unconditional obligations. |
| EQ-15 | Final installed-package bootstrap/growth/export tests, actual provider run and closed-Foundation revalidation; prior full phased execution remains its own evidence. |
| EQ-16 | Raw phased BRD preserved with source hash, questions and cross-references; source claims do not become approvals. |
| EQ-17 | Foundation complete while later features/decisions remain visible and unauthorized; native acceptance and unchanged repeat closure. |
| EQ-18 | Precise persistent replay, full readback and orderly two-process restart; offset-free timestamps use local date-specific timezone rules, gaps/overlaps require explicit UTC. |

## Shortcoming-to-mechanism map

| Gap | Prevention/detection | Evidence |
| --- | --- | --- |
| SC-01 | Readability standard plus cohesive responsibility examples and source-aware review | IP-08/20; EQ-05 |
| SC-02 | Aggregate partial-type complexity/coupling; ownership review | Partial-type negative fixture; EQ-05 |
| SC-03 | Explicit applicable code-quality guidance and native analyzer/format gates | IP-03/08; native build/lint evidence |
| SC-04 | Native architecture boundary tests | Known violation/restored sequence; EQ-05 |
| SC-05 | Language defaults and framework-aware conservative discovery | IP-02/05; EQ-02 |
| SC-06 | Established runner, small fixtures and readable reference harness | IP-05/20; native export receipts |
| SC-07 | Precise independently diagnosable assertions and native test identities | Migration seeded defect/restored pass; TRX and diagnostic records |
| SC-08 | Required coverage/mutation scope, reviewed exceptions and distinct review | IP-05/15/18; EQ-04/09/11 |
| SC-09 | Native suite/report reconciliation bound to source and task inventory | Candidate-32 selector/workflow regressions; Foundation TC automation |
| SC-10 | Versioned recipe versus ignored local outputs, ownership-aware upgrades | Automatic-examples fixtures and 92-file export binding |
| SC-11 | Visible workflow/check applicability, native results and omissions | IP-04/19; EQ-11/14 and disclosed suite scope |
| SC-12 | Iteration graph, dependency/guidance reconciliation, all-standard reassessment | IP-02/03/17; final empty-growth smoke |
| SC-13 | Requirement → task → implementation → verification → review identity | Foundation task receipts; EQ-13/16/17 |
| SC-14 | Independent required inventory and six gate states | Completion-context/transition native negatives; EQ-11 |
| SC-15 | Reviewer callers/contracts/operational evidence and explicit omissions | Actual interval caller review; Foundation independent reviews |
| SC-16 | Graph/source/context freshness and source fallback | Native stale/context controls; final graph and closure refresh |
| SC-17 | Declared representative workload, full output, exact values and recovery | 500-reading business/restart receipts; EQ-07/18 |

## Limits of the readiness claim

This is a **Windows .NET/C# new-project trial**, not TypeScript/Python or cross-platform qualification.
The final run covers affected suites and bounded package smoke, not a fresh whole-solution regression.
Mutation qualification is bounded to declared selected production scope; supplementary scanner parsing and browser
accessibility checks are limited as documented. The restart scenario is orderly process restart, not forced-kill,
database-crash or power-loss recovery. Model reviews are advisory and can miss defects.

Native Claude command rules add permissions; native normalization/compound commands/directory changes can still
match them. They do not replace a sandbox or constrain build scripts. No trust database, global bypass or broad
shell permission was added. Missing prerequisites, unqualified native runner binding and optional diagnostics
remain visible; none is converted to passing project evidence.

The trial does not approve the supplied sample BRD, implement an investment application, activate operations,
promote the candidate into the active installation, merge, or publish. Future product scope and approval decisions
belong to the new project. The ready trial guide preserves those boundaries.

# Remaining acceptance — 9 October

## Current disposition — 9 October, candidate 34

**Ready for a new .NET/C# project trial on Windows.** The headless Claude command-permission flow and actual second-provider skill qualification are complete. The installed candidate executes the three reviewed skill commands; a separate denied-command control correctly fails. Final affected checks, clean declared/growth bootstrap, 57 exported native unit cases, package binding and closed-Foundation revalidation pass. Independent source/readiness and unfamiliar-reader reviews have no remaining corrective findings. The implementation-plan checklist is reconciled.

Use the [final readiness record](engineering-defaults-trial-readiness.md) for the IP/EQ/SC mapping, native evidence and precise limitations, and the [Windows trial guide](engineering-defaults-dotnet-trial-guide.md) for next-project steps. The active installation is unchanged; this does not approve the sample BRD or later application scope.

## Historical qualification checkpoints

The earlier open/blocked statements below describe their original candidates and are superseded by the current disposition above. Their receipts and failures remain retained.

**Not ready for a new .NET/C# project trial yet.** Foundation is accepted and closed, and the synthetic workload tests pass. The user resolved the timestamp policy on 9 October: offset-free CLI timestamps are local time. Actual command execution through the second agent's portable skill remains open. Final qualification, package binding and checklist reconciliation also remain required before readiness. The active installation is unchanged.

## Completed scope

The user's instruction to complete the remaining tasks authorized the previously listed final Foundation acceptance. Candidate 31 reran native verification, recorded Andrew Spiteri's acceptance of the already verified Foundation scope and closed CIS-0001. The original baseline and seven completed task receipts remain intact. Receipts are retained under `.cis/local/qualification/foundation-rehearsal-23/evidence/`: `pre-acceptance-31.json`, `foundation-human-acceptance-31.json` and `foundation-closed-31.json`.

The reviewed Foundation CLI, precise JSON input validation, bounded process capture, architecture tests and correlated test diagnostics are now in the distributable .NET example. Native xUnit remains the test framework. The explicit package inventory includes the new helpers and test sources. PowerShell 7 is now declared alongside .NET 10 and Docker as a prerequisite for the diagnostic process checks.

The source example passed 46 unit, 63 business and nine API/browser tests, with no failures or skips. Both native solutions build with zero warnings and errors; native format checks pass. The retained workload receipts establish:

| Native case | Result |
| --- | --- |
| 500 readings, duplicate/out-of-order replay, conflict rollback and complete readback | 9.5874245 seconds against 60 seconds; exact total 0.00125250. |
| Two CLI processes with retained database, first 250 readings then replay of all 500 | 7.6838283 seconds against 60 seconds; exactly 250 additional readings accepted; all 500 values read back; exact total 0.00125250. |

The restart is orderly process exit and restart, not forced termination or database crash recovery. These are synthetic example receipts, not completion records for separate HLT-FR-002/003 features or acceptance of a future application's workload. Browser behavior is separately demonstrated by the example; it is not added to Foundation's approved CLI-only scope.

## CIS corrections

- An unchanged approved feature can refresh its derived test catalogue without rewriting plan bytes or authored task handoffs. Changed task-generation inputs that would alter the approved plan block before catalogue or task writes.
- Workflow parsing rejects malformed rows, duplicate/missing headers, unsafe step IDs, invalid continuation policies and invalid timeouts before running commands.
- Repository Doctor and native coverage reconciliation use the same scoped collector-selector grammar.
- Repository Doctor detects newly discovered components missing ownership in a customized profile. It follows the graph parser's component-heading, root and duplicate-ID rules and preserves authored guidance.
- Codex JSONL execution treats malformed provider events as invalid evidence, even if the process exits successfully.
- Claude reviews with disabled session persistence restart with complete current context instead of attempting to resume a nonexistent provider session.
- Claude results containing denied tool permissions are classified as Failed with `permission-required`, even when the provider exits successfully. The diagnostic preserves the blocker without repeating denied command arguments.

Independent source review accepted the initial corrections after two findings were fixed: task-generation drift during catalogue refresh and inconsistent profile-root parsing. Claude's separate source review then found that a root-level infrastructure declaration could hide new projects; the matching rule and a regression fixture now address that finding. Claude's final source-only closure is ready with no unresolved corrective findings. Native Debug qualification passes 115 affected Delivery cases, 263 Repository cases, 35 Testing cases, 17 Host cases and 228 Agent cases. Two Unix FIFO cases are skipped on Windows. The full Agent result precedes the final permission-denial correction; its focused Release successor passes 30 cases. Final Release qualification passes 264 Repository cases (two Unix-only skips), 35 Testing cases, eight focused ownership/selector cases, nine malformed-workflow cases and two plan-preservation/drift cases. Focused runs overlap full suites and are not added together. These are affected-suite results, not a new whole-solution or cross-platform pass.

The first preservation regression exposed CRLF plan rewriting; a profile fixture initially omitted explicit adoption of human customization. Both were corrected and rerun. An initial coverage filter selected zero tests and is not counted; the complete 35-case Testing run supersedes it. Earlier failures remain retained. The first combined example run overwrote the unit TRX with the business TRX; a separate 46-case unit run supplies its own retained receipt.

## Open release conditions

**Second agent's executable skill:** fresh Claude run `RUN-20261009020946-8BCC27950E` read the portable skill, source and caller. Its command permission layer required approval for `dotnet build -c Release`, so build, CLI help and boundary execution did not run. Candidate 31 recorded a successful provider response; that historical classification is not a successful skill qualification. The corrected provider now rejects reported permission denials. Source-only detection of the deliberate interval defect does not meet EQ-08. No trust records or permission bypasses were applied.

There is no pending approval request to find in Claude. The retained execution run has ended. CIS starts Claude with non-interactive `-p`/`stream-json`; the adapter declares no interactive-permission support and uses `acceptEdits` for workspace-write. That mode did not authorize the fixture's shell commands. CIS's current `--approve-requests` handling covers supported Codex requests, not Claude command requests. The completed source review is separate from this blocked executable-skill qualification.

The remaining engineering task is to provide a supported, bounded permission route for the selected provider, then rerun the synthetic skill and retain actual command output. Qualification needs exactly the fixture build, CLI help and interval-boundary query already listed in the skill. Any execution permission must stay within that reviewed scope; a successful review or a broad permission bypass cannot replace execution evidence. A separately launched interactive Claude session is a possible manual workflow, but it is not an approval request currently waiting in CIS.

Claude's other review observations remain explicit: the drift guard compares the canonical plan projection, not changes to provider prose made without a version change; workflow definitions treat subsequent pipe tables as part of the step schema; malformed or blank JSONL lines are rejected; and the generic continuation prompt still mentions a retained session even when the review adapter correctly restarts. Pre-existing capability-conflict event persistence can occur before the plan drift guard; the guard's claim covers catalogue/task writes, not every possible diagnostic write. The permission-denial tests cover parsing and classification, while the sticky multi-event flag and failure precedence were source-reviewed. These do not establish broader task-template equivalence or relaxed parser compatibility.

## Local-time policy — candidate 33

The user explicitly selected local time for timestamps without an offset. The CLI now interprets them in `TimeZoneInfo.Local`, using the timezone rules for the supplied date, and converts them to UTC before application validation and persistence. Explicit `Z` and `+00:00` remain UTC; explicit nonzero offsets retain the previous rejection behavior. DST gaps and overlaps require explicit UTC, because the local text does not identify one instant. Out-of-range conversion and finer-than-microsecond precision are rejected. This change applies only to CLI file input; it does not change HTTP or application contracts, the accepted Foundation dossier, or any investment-product implementation.

Candidate `0.3.0-engineering.20261009.33` is installed at `.cis/local/engineering-tool-rehearsal-33/cis.exe`, with package SHA-256 `bd202f4667a0535b21f35c65aef86d411dd4e2d52003b60e3bc0d06d45c005a7`. All 42 CIS assemblies and 92 bundled example files match the build/source inventory. Native source tests pass 57 unit and 64 business cases; the installed-tool export separately passes all 57 unit cases and the local-time CLI/PostgreSQL persistence-and-UTC-replay case. Build, analyzers and formatting pass. An initialized fixture containing an exact copy of the updated portable skill passes strict validation and deterministic audit. Source review and a separate reader check found no remaining corrective issue after updating CLI help and clarifying precision wording.

The initial business attempt passed 63 cases and failed one existing parser case because its child process could not start CoreCLR (`0x800705AF`). All six parser cases and the full 64-case suite passed on retry without weakening assertions or deadlines. The cause of that runtime startup failure is not established; its report remains retained. The first build also caught missing explicit `DateTimeKind` and uncached serializer options in new test code; those were corrected under the existing analyzers. The standalone example lacks a CIS repository configuration, so direct skill validation there returned an invalid-configuration result; the configured fixture supplies the actual passing validation. Evidence is retained under `.cis/local/qualification/local-timestamps/` and the source/export's native result directories.

The timestamp decision is closed. Cross-machine replay should use the explicit UTC instant, because offset-free text depends on the executing machine's timezone. The second-agent executable-skill qualification remains blocked by the CIS headless Claude permission-flow gap, and no active installation was replaced.

## Historical isolated candidate 32

Package `0.3.0-engineering.20261009.32` has SHA-256 `0a84e7a77671bafa4e159dc85c112401b4446ace10cce1eb9aa89fc04ba71e4c`. It is installed only at `.cis/local/engineering-tool-rehearsal-32/cis.exe`. All 42 packaged CIS assemblies match the Release build, and all 90 recipe files match source. Five affected Plan, Workflow, Repository and provider assemblies also match their tested Release copies. The 90-file installed-tool export matches the package byte-for-byte and independently builds with zero warnings/errors, passing 46 unit and 63 business tests. Those reruns do not add distinct tests to the source-example counts above.

Strict repository documentation validation passes with 525 catalogued documents. All 53 skills pass strict validation and deterministic audit. A fresh declared-C# bootstrap installs all 90 matching example files automatically; its graph builds, and repeat initialization has no creates, updates or collisions.

The source repository's closing alignment check initially found a customized-guide collision. The current generated local-example section was merged while preserving the stronger authored completion guidance; native adoption retained the merged guide byte-for-byte. Generated command references and the starter manifest were reconciled, and the repeat preview has no creates, updates or collisions. Final graph build and strict validation pass with fresh evidence. Doctor has zero errors and ten disclosed warnings: missing optional normalized API/reference/index state, unavailable remote CI credentials, existing Draft/TODO documents, unevaluated explicit MSBuild imports and the conservative unqualified runner binding. The native example runner was executed explicitly; discovery did not infer that qualification or generate a guessed command. These warnings have not been relabelled as passes.

Packaging and export success do not resolve the executable-skill acceptance condition or approve promotion of this candidate.

The original fixture and the first fresh copy also stopped at Git baseline checks. The latter belonged to the sandbox account while the provider ran as the user; a new fixture created under the provider's account passed Git's normal ownership checks. All attempts are retained rather than replaced.

## Evidence and continuation

Current CIS reports, provider attempts and package checks are retained in `.cis/local/qualification/remaining-acceptance/`. Example reports are in `examples/dotnet-engineering/.cis/local/results/remaining-*`; input, readback and diagnostic receipts remain in the example's unique business/web evidence directories. The native reports are authoritative; this document states their scope.

After the executable-skill condition is resolved, rerun the affected checks, bind the final package and export to their tested sources, and reconcile the implementation plan's IP/EQ checklist before declaring trial readiness. Existing mutation scope, partial supplementary scanner parsing and Windows-only qualification remain explicit limitations. This work does not approve a sample BRD, implement an investment application, merge or publish CIS, or replace the active tool installation.

# Technical direction for the disposable rehearsal

Andrew Spiteri approved these 16 technical choices on 6 October 2026 with “go ahead with the proposed choices”. They were recorded through the supported questionnaire commands for the disposable foundation and replay rehearsal. This selects no technology for the real project and does not establish successful task closure. Canonical technical-intent activation is a separate recorded lifecycle result below.

| Area | Approved direction and basis |
| --- | --- |
| Product surfaces | Local command-line application and native test processes for foundation and replay. Browser inspection and external activation remain blocked later work. |
| Frontend | No frontend in the current rehearsal. Do not select a future browser framework or budget as part of this scope. |
| Backend | Reuse the example's C#/.NET 10 code, System.CommandLine interface and pinned dependencies. SDK 10.0.401 is the qualification environment. |
| Architecture | Reuse the existing Core, Persistence and CLI responsibilities. Business behavior depends on a persistence interface; the CLI composes the concrete adapter. No shared application framework or new test runner is introduced. |
| Ownership | The existing disposable authority owns governance; the registered application participant owns the native fixture. One local executable and its test projects suffice. |
| Transactional data | Reuse the example's disposable PostgreSQL 16.8-alpine container. Per-reading persistence and conflict behavior are observed example facts; no production database is selected. |
| Supporting stores | No additional cache, search, queue, object store or analytics service is needed for this bounded fixture. |
| Contracts | Preserve the example's input and versioned JSON result, exact decimal quantities, stable reading identity, UTC event time, exit codes and replay semantics. No external integration is activated. |
| Identity and access | Local operator and test-generated disposable database credentials only. No application account, tenant, analyst authorization model or external activation role is created. |
| Hosting | Isolated Windows working copy, local .NET processes and disposable Docker database. No deployment or global tool replacement. |
| Background work | Replay is explicitly invoked and awaited. No scheduler or message broker is added. |
| Operations | Retain the native test fixture's correlated logs, metrics, traces, workflow output and partial evidence. Preserve declared timing boundaries and observed recovery limitations. |
| Security | Use synthetic input and disposable credentials. Keep connection values out of reports. Run applicable existing native scanners and security checks; absence of a live account is not a security-test pass. |
| Quality | Reuse native unit, architecture, business/integration, coverage, mutation, analyzer, compatibility, security and performance mechanisms where applicable. Every required completion gate must carry current actual evidence or supported inapplicability. |
| AI | No model participates in application behavior. CIS's authoring and independent-review agents remain engineering tools with native provenance. |
| Exclusions | No real BRD import, operational data, external account actions, new browser choices or claim of product/release completion. Future decisions retain their explicit deferrals. |

The fresh candidate-7 export passed both 500-reading workloads: 7.0722305 seconds for the service/connection-restart case and 5.2622129 seconds for the two-process CLI restart case. The latter saves 250 readings in one process, waits for exit, then replays all 500 identities with duplicates and reversed order in a second process. Both process lifetimes and independent full readback are timed; Docker startup, input preparation and final report writing are excluded. Abrupt termination and database crash recovery are not claimed. Exact aggregate totals are qualified for this dataset; the example does not guarantee exact sums for arbitrary near-limit decimal quantities.

All 16 answers are recorded under Andrew Spiteri through cis technical-intent questions answer. Exact native outputs are retained as phased-technical-answer-TI-Q-001.json through TI-Q-016.json in the local qualification directory. The original proposal remains unchanged as the reviewable historical input. The refined technical document preserves the questionnaire, baseline and decision identities and spells out current responsibilities, unsupported recovery claims and deferred boundaries. Validation reports Ready for Approval, current and valid, with no warnings after a strict graph check; independent source-backed review follows before activation.

Canonical technical intent is now **Active**, recorded by the supported approval command under Andrew Spiteri after current validation, strict graph checks and exact reviewed-snapshot comparison. Final independent review RUN-20261006115838-81B7F5F83A returned ready with no findings. Source-backed refinement clarified that the fixture obtains its connection from the pinned builder without claiming per-run credential randomization. The original questionnaire remains unchanged. The subsequent architecture stage is tracked in the rehearsal record and is not implied complete by this approval.

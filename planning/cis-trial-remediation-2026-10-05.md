# CIS remediation and verification record

Date: 5 October 2026

Six CIS improvements are implemented and regression-verified locally. This public
record describes CIS behavior and its synthetic regression fixtures. It excludes
application-specific review evidence.

## Implementation record

| Finding | Implemented behavior | Regression coverage | Status |
| --- | --- | --- | --- |
| CIS-TRIAL-001 | Shared opening-front-matter reads, writes, removals and approval normalization preserve body examples and unrelated YAML maps. Implicit priorities retain reviewed backlog values. Active backlog semantic changes return candidate items without changing backlog/catalog until `--apply-reviewed-changes` is used; renewed approval remains separate. | LF/CRLF preservation, literal values, missing fields, historical-content digest, repeated `Could` priority, candidate-only update. | Implemented |
| CIS-TRIAL-002 | API comparison traverses nested objects, arrays and local references. Cycles, composition, unresolved/external references, nullable semantics, aggregated response variants and unsupported constraints report explicit partial coverage. Removing a required response guarantee is breaking. Partial comparison exits 4 unless a known breaking finding requires exit 5. | Synthetic nested additions, nested removals/type/required changes, references, arrays, recursive and unsupported schemas. | Implemented |
| CIS-TRIAL-003 | Graph compilation analysis persists under a source/ownership/compiler/runtime/build identity. Documentation edits can reuse unchanged compilation. Repository-local locks serialize graph/index builds; followers recheck caches. Index phases emit optional timing diagnostics. | Cache equivalence, source/owner invalidation, corrupt-cache recovery, concurrent graph builders, documentation-only refresh. | Implemented; application-scale benchmark remains follow-up |
| CIS-TRIAL-004 | `--summary` reduces API diff details and JSON workspace/definition payloads while retaining state and diagnostics. Savings candidates require matching command ownership. Large-output opportunities remain visible despite registered estimates; output expansion is explicit. | Composite estimate isolation, oversized output, blocked/stale snapshot summaries, definition summary parity. | Implemented |
| CIS-TRIAL-005 | Shipped guidance uses `cis generate`; `index find` accepts `--query` as an alias. Recorded parser errors, explicit plan blockers and execution failures are distinct. Usage records add optional build identity without raw argument values or output. | Parser classification, structured blocker/build identity, CLI help and alias smoke checks. | Implemented |
| CIS-TRIAL-006 | Volume-sensitive plan text raises an early representative-workload prompt. An explicit workload receipt records shape, size, resource limits, software/fixture identity and differences, separate component/installation/end-to-end results, and an artifact hash. Final-sweep completion requires satisfactory applicable evidence or a concrete inapplicability rationale. | Component/installation passes cannot mask timeout; missing receipt/artifact, changed hash, and explicit inapplicability; final-sweep gate. | Implemented |

### Implementation locations

- Metadata: `src/Cis.Abstractions/CisFrontMatter.cs`, BRD service/digest/backlog helpers, technical-intent service.
- Compatibility: `src/Cis.Modules.Api/OpenApiCompatibilityService.Schemas.cs`, compatibility service/contracts and API command renderer.
- Derived-state performance: `src/Cis.Abstractions/CisBuildLock.cs`, `src/Cis.Modules.Graph/CSharpAnalysisCache.cs`, graph/index builders.
- Compact output and usage: repository workspace snapshot, definition/API commands, feedback contracts/store/service, scoped estimator registrations.
- Command outcomes: `src/Cis.Abstractions/CisInvocationOutcome.cs`, host invocation capture and plan renderer; index command alias and engineering-assurance guidance.
- Workload readiness: `src/Cis.Modules.Plan/WorkloadEvidenceReview.cs`, planning validation and final-sweep transition.
- Affected command manuals, specifications, repository starter content and feedback/planning skills were updated alongside the implementation.

### Verification record

The unpublished commits were consolidated before publication to exclude private project evidence from outgoing history. Verification completed on 5 October 2026, on Windows with .NET 10.0.12.

- `dotnet build ChangeImpactStudio.slnx --no-restore --disable-build-servers -m:1`: passed, zero warnings/errors. Subsequent focused builds compiled the final API/metadata refinements; the committed host build also passed with zero warnings/errors.
- `dotnet test ChangeImpactStudio.slnx --no-build --no-restore --disable-build-servers -m:1`: 1,076 tests passed across 26 suites; zero failed or skipped. The full run began before the last API/metadata refinements. Those refinements were separately rebuilt and checked: API 24 passed, abstractions 53 passed, and BRD preservation/candidate/no-work regressions 3 passed. The full graph suite used the final lock/cache implementation and passed 63 tests.
- `cis docs validate --strict --format agent`: 492 catalog entries/documents, zero warnings/errors.
- `cis skills validate --strict --format agent`: 49 skills, zero warnings/errors. `cis skills audit --no-llm --format agent`: clean; zero duplicates/overlaps/conflicts, no quarantine.
- `cis standards validate --strict --format agent`: 7 standards, 64 rules, zero warnings/errors.
- Changed command surfaces were smoke-checked through help: API/definition/workspace summaries, backlog reviewed-change application, and generate templates/describe/validate/render. `index find --query` executed successfully with a no-match fixture.
- `git diff --check`: passed. Initial concurrent MSBuild attempts encountered resource/file-lock failures; final validation used a single MSBuild worker and did not leave those failed attempts as the acceptance result.

- All 322 extension tests and JavaScript syntax checks passed. The host's npm launcher was broken; the same package-script steps ran directly through Node.
- A synthetic fixture with 100 C# source files measured 1,145.6 ms for fresh compiler analysis and 15.7 ms for cached analysis in one local run. Analysis results were identical. This is an isolated cache measurement, not a statistically controlled benchmark or an application-wide speedup claim.
- Final local index refresh covered 1,302 candidates with zero pending files and no reported errors, using Ollama `qwen2.5-coder:1.5b`. No remote provider was authorized or used. The build output does not quantify source truncation; routing cards remain non-authoritative and source files were inspected directly.
- Retained local command logs are under `.artifacts/trial-*.log` in the CIS checkout. These are disposable verification outputs; the durable result is summarized here.

### Limits and rollout

- Validation used the CIS checkout; application deployment remains separate. The changes were exercised through the CIS Debug build and temporary fixtures. Deployment and an application-level smoke run remain separate work.
- API partial coverage is intentionally conservative. Composition, recursive/external references and unsupported constraint semantics require review; the tool does not claim complete OpenAPI compatibility certification.
- The graph cache invalidates the entire compilation after any C# source or ownership edit. Fine-grained compiler invalidation and representative application growth benchmarks remain future performance work. Locks wait up to five minutes before surfacing an I/O failure.
- Summary output is opt-in. Full output remains available; diagnostics can still be large. Pagination and changed-only output are not implemented by these fixes.
- Workload detection is a keyword-based prompt, not a complete classifier. Explicit profiles are checked regardless of detection. Receipt validation checks declared fields and artifact integrity; a declaration of success is not independent proof of workload correctness or human acceptance.
- Writing a candidate backlog does not approve it. Existing current-approval and human-decision gates still apply.

This is a product remediation record. Examples are synthetic; application names,
source paths, private implementation details and private verification artifacts are
excluded. The recorded CIS checks do not establish downstream application acceptance.

## Publication documentation check

On 5 October 2026, command references were reconciled with the latest source and
16 documented command surfaces were checked against executable help. Corrections
cover compact-output options, document copying, reviewed backlog preservation,
story task commands, empty-folder Git setup, telemetry fields and workload gates.
Strict documentation, skills and standards checks passed with zero warnings/errors.
The neutral API fixtures passed all 24 tests; the neutral question-suggestion
fixtures passed all eight tests. Private application references and retained
application-specific evidence were excluded from both the public tree and the
unpublished history prepared for delivery. Detailed review evidence remains local
outside the public repository.

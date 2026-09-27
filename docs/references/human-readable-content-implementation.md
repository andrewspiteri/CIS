---
title: Human-readable content implementation and verification
type: reference
status: Draft
owner: Repository maintainer
last_reviewed: "2026-09-21"
review_cadence: on content policy change
cis:
  stable_id: change-impact-studio:reference:human-readable-content-implementation
---

# Human-readable content implementation and verification

This records engineering work against the current checkout, adapting the eight proposed files in the supplied CIS readable-content draft pack. It is evidence of integration and review, not stakeholder approval, a release, or proof of semantic equivalence. The [shared standard](../standards/human-readable-content-standard.md) and [terminology reference](human-readable-content-terms.md) remain Draft.

## Guidance and starter integration

The three skills divide technical writing, interface writing, and content review. Existing documentation, BRD, technical-intent, solution-design, feature, impact/design review, and frontend guidance links to them. Existing mandatory structures, hidden BRD evidence, privacy rules and approval gates remain controlling. Design guidance now preserves evidence-backed uncertainty instead of treating “may” and “might” as universally removable filler.

`AGENTS.md` routes repository work. Generated `AGENTS.md` and `CLAUDE.md` explicitly route supported host entry points; file-scoped instructions route applicable editor work. `AgentService.ExecuteRun` also supplies the policy in the actual invocation envelope for Codex, Claude and continuation/review paths. Tests inspect dispatched requests; installing a skill file is not considered proof of runtime loading. Live provider execution was not used for this verification.

`RepositoryStarterBinder.HumanReadableContent.cs` binds the standard, terms, fixtures, three complete skills and instructions to the target identity and chosen documentation root. UI instructions select detected frontend, mobile, native or VS Code components; a backend-only product does not receive CIS-specific UI paths. The portable UX skill is included because other skills link to it. Changed managed guidance uses template version 2. Existing reconciliation upgrades matching managed files, reports divergent-file collisions, and preserves human-owned artifacts. Applied manifest hashes in this checkout were not edited.

The CLI embeds the eight starter resources. `Cis.Abstractions` separately embeds the same standard for trusted runtime excerpts. No runtime policy is loaded from the target repository. New guidance is not silently merged into an existing, divergent `AGENTS.md` or `CLAUDE.md`; the existing collision/review workflow applies.

## Generation inventory

The inventory follows all text-generation service/provider references under `src/`, the agent dispatch path, and related deterministic authoring paths. The policy applies to prose fields without changing structured response contracts.

| Path | Policy and output | Derived identity and limits |
| --- | --- | --- |
| `Cis.Modules.Agent/AgentService.ExecuteRun` | Common policy for document authoring and explanations; review excerpt for BRD review. Existing task contracts and protected evidence remain in the envelope. | No new output cache. Codex/Claude invocation, permissions and continuation contracts unchanged. |
| `Cis.Modules.Brd/BrdSourceSummaryService` | Overview excerpt for bounded local source overviews and extractive sentence selection; JSON-quoted untrusted source. | `brd-source-summary-v3-hc-1`; existing 45-second/450-token overview and 30-second/120-token selector bounds retained. Safe excerpt fallback and provenance checks retained. |
| `Cis.Modules.Brd/BrdQuestionGuidanceService` | Business-reader review excerpt for suggested answers, with separately quoted question/context evidence. | Policy revision enters suggestion input digest; canonical BRD SHA and approval identity unchanged. Existing JSON, retry, prompt-size, timeout and disclosure limits retained. |
| `Cis.Modules.SolutionDesign/FeatureArchitectureGenerator` | Feature-review excerpt for selection, component labels, descriptions and relationships; source evidence is explicitly marked, with quoted source/direction passages. | `feature-architecture-5-hc-1`; existing schemas, affirmative endpoint checks, local-only route and output/time budgets retained. |
| `Cis.Modules.Design/FeatureScreenGenerator` and `.Review` | Interface excerpt for screen selection, screen plans and saved human amendment requests. Source is quoted evidence; amendments refine proposed designs without granting execution authority. | `feature-screens-4-hc-1`; previous previews/feedback survive failure. Raw input credential checks precede disclosure; no remote fallback. Existing schemas and total/per-call budgets retained. |
| `Cis.Modules.Security/SecurityService.SecurityPrompt` | Review excerpt; bounded, redacted findings quoted as evidence. | Existing prompt digest includes policy; local-only request and 700-token bound retained. Findings remain advisory. |
| `Cis.Modules.Skills/SkillAuditService.CreatePrompt` | Review excerpt for skill comparisons; names/descriptions/bodies quoted as evidence. | No generation-result reuse. Existing provider selection and disclosure authority unchanged; validation here uses `--no-llm`. |
| `Cis.Modules.Standards/StandardAuditService.CreatePrompt` | Review excerpt for standard comparisons; bodies quoted as evidence. | No generation-result reuse. No automatic compliance or approval claim. |
| `Cis.Modules.Ai/AiGovernanceService.Evaluate` | Common policy, prose fields only, before the caller's existing task/output contract. Caller prompts remain requests; this generic API does not reinterpret their requested schema. | Existing prompt hash/cache key now includes the policy. Old entries remain stored but cannot satisfy the new request. |
| `Cis.Modules.Index/FileIndexService` | Exempt: compact structured agent-routing cards, not human-facing narrative. | Existing routing-card version and small output budget unchanged. |
| `Cis.Modules.Ai/AiQualificationService` | Exempt: exact probe, benchmark and regression prompts; adding prose instructions would corrupt the measurement. | Qualification contracts unchanged. |
| `AiTextGenerationService`, provider adapters, module registration, abstraction contracts | Transport/registration, not authoring. | No blanket provider-level injection or remote fallback. |
| `Cis.Modules.Generate/GenerateService`, deterministic definition/questionnaire/report renderers, diagnostics and learning proposals | Deterministic templates and checked-state output, not a model call. Authoring templates route to shared guidance; technical-direction action text is clarified at its domain projection. | Commands, schemas, machine identifiers, exit codes and approval decisions preserved. |

`HumanReadableContentPolicy` extracts bounded named sections from the shared standard. Revision `hc-1` must be advanced when those runtime instructions change. Its tests assert packaged availability, supported sections, length, protected facts and lossless evidence quoting. Review fixtures remain examples: conditional cases are still conditional.

## Source-aware review

The implementation agent compared the changed content with the actual renderer inputs, action handlers and services. This is source-aware model review, not a human approval or universal semantic comparison.

| Content | Protected facts checked | Result |
| --- | --- | --- |
| Starter documents and skills | Target identity/root, Draft status, required BRD sections, protected comment blocks, human ownership and explicit approval | Preserved; fresh/repeat/custom-root/upgrade/conflict tests cover the binding. |
| Technical decision | Suggested answer is not recorded; save may succeed while readiness refresh fails; approval is separate | Recovery now gives Refresh priority and labels old details potentially stale. `save-technical-decision`, review token, stale-edit handling and approval handlers remain intact. |
| Draft from code | Reads selected product-owned repositories and updates the technical-intent narrative; cannot resolve human choices or approve | “Infer from repos”/“Refresh document from repositories” becomes “Draft technical direction from code,” with effect and unavailable-state explanations. |
| UI direction | Preview availability is not approval; saved human answers survive refresh; direction and preview updates have prerequisites | “Preview available · Direction pending” becomes “Preview available · UI direction needs review.” The refresh explanation states prerequisites and subsequent review. |
| Feature review | An empty findings list does not imply checks ran or the page was reviewed; definition review does not authorize implementation | Incomplete/stale pages retain blockers and action IDs. No findings, not run, failed and stale are distinct. |
| Source overview | Full source, sampled input, cached policy version and supporting-quotation limits; no remote fallback | “Local model summary” becomes “Local model overview.” The panel says quotation matches do not verify every claim. Full-source action and source ID remain. |
| Recovery | Checked error kind, unknown cause, diagnostic redaction, interruption with potentially completed changes | Deterministic explanation plus safe diagnostics; original logged diagnostic shape, error kind and exit code retained. |
| BRD and canonical authority | HTML evidence blocks, managed metadata, source mappings, source SHA, reviewer/approval state | Runtime prompts explicitly preserve them; existing BRD and agent regression assertions remain. No canonical product document was bulk-rewritten. |

All 13 [review fixture cases](human-readable-content-fixtures.json) were inspected as advisory examples. HC-FIX-04 remains `context-dependent`; HC-FIX-05 and HC-FIX-09 remain `accept-subject-to-domain-check`. Added examples reject “not run” becoming “no findings,” unsupported failure causes and refresh becoming approval. Fixture parsing is tested separately from actual generation/rendering behavior.

## Independent reader check and presentation

A separate context-limited model reviewer received only five synthetic rendered screens/text files and realistic task questions. It received no source, expected answers or authoring notes. It identified setup before import, evidence review before technical save, preview versus approval, business approval before technical approval, and successful save versus failed readiness refresh. These matched the source-backed expectations above.

The first pass found conflicting recovery guidance, “Available” above disabled actions, and a sticky footer obscuring content in desktop captures. Corrections prioritize refresh after partial success, explain unavailable actions and keep the footer in document flow. The second pass confirmed those issues resolved. Its remaining question about UI refresh versus preview led to an explicit prerequisite-based explanation, verified against `DefinitionWizardService.Prepare` and `RefreshDerived`. A final context-limited check of that clarification found no material ambiguity preventing the next action. These are advisory model findings; no real-user usability testing occurred.

[`tools/render-readable-content.cjs`](../../tools/render-readable-content.cjs) uses production HTML renderers and fixed domain fixtures. It renders baseline `HEAD` and working-tree versions in headless Chromium with a fixed VS Code theme fixture, captures actual output, and checks horizontal overflow, visible-control names, keyboard focus and Tab reachability of visible enabled controls, including questionnaire disclosure summaries. Five scenarios × three views × before/after produce 30 PNGs, HTML and extracted text. Views are 1000px, 320px and a 640px viewport at CSS zoom 2. CSS zoom approximates 200% zoom; it is not a live VS Code host zoom test. Results and captures are local under `artifacts/readable-content/rendered/`.

Manual follow-up in a live VS Code host: open Getting Started in a narrow editor; complete initialization preview without approving it; open a technical decision with missing/stale review evidence; simulate a saved answer with failed readiness reload; tab through buttons, fields and details; zoom the editor to 200%; verify UI refresh findings and source navigation; verify technical approval remains blocked before business approval. These host and assistive-technology checks remain unverified.

## Validation commands and results

Commands below run from the repository root unless stated otherwise. `dotnet src/Cis.Host/bin/Release/net10.0/cis.dll` is the CLI built from this checkout. Local logs live under `.codex-tmp/content-*`; they are disposable evidence, not canonical product records.

| Command/check | Result |
| --- | --- |
| `dotnet build ChangeImpactStudio.slnx --no-restore -c Release` | Passed, zero warnings/errors. |
| `dotnet test ChangeImpactStudio.slnx -c Release --no-build --no-restore --maxcpucount:1` | 795 tests across 26 projects pass using the complete run plus focused reruns below; no skipped tests. |
| Built CLI `docs validate --strict --format agent` | Passed, 487 documents/catalog entries, zero warnings/errors. |
| Built CLI `skills validate --strict --format agent` | Passed, 49 skills, no warnings/errors. |
| Built CLI `skills audit --no-llm --format agent` | Clean; 8 candidate pairs, no deterministic duplicate/overlap/conflict findings; model disabled, no quarantine. |
| Built CLI `standards validate --strict --format agent` | Passed, 7 standards, 64 rules and 64 conformance entries. |
| Built CLI `graph build --format agent`, then `graph validate --strict --format agent` | Final build passed: 15,149 nodes, 72,120 edges, zero build warnings/errors. Strict validation is fresh with zero errors and one pre-existing identity warning (exit 5); baseline evidence is recorded below. |
| `node --check` for extension entry point and all `lib/*.js`; `node --test vscode-extension/test/*.test.js` | Passed, 193 tests, no skips or failures. Same checks as the package's check script, with broader syntax coverage. |
| `node tools/render-readable-content.cjs` (Playwright available through `NODE_PATH`) | Passed, 30 captures; no working-tree horizontal overflow, unnamed controls, missing focus indication or unreachable visible controls in the sampled views. |
| `node tools/audit-dotnet-packages.mjs ChangeImpactStudio.slnx` | Passed: no vulnerable or deprecated package findings. Sandbox initially blocked NuGet configuration access; approved retry completed. |
| `dotnet pack src/Cis.Host/Cis.Host.csproj -c Release --no-build --no-restore -o artifacts/readable-content/package` | Passed; built local 0.3.0 tool package. |
| `tools/package-vsix.ps1 -Output artifacts/readable-content/vsix` | Passed; packaged extension. Final package refreshed after reader corrections. |
| Local-only tool install, then packaged `host modules`, `repo init --root handbook/cis --dry-run`, confirmed init, strict docs/skills/standards and repeat init | Passed outside the source directory in a disposable editor repository: 49 documents, 46 skills, 5 standards/53 rules; repeat unchanged. Both host entry points and all content dependencies present; no leaked CIS extension scope. |

The local-only package install used a configuration containing only the built package directory, an isolated package cache and tool directory under `artifacts/readable-content/`. It required sandbox approval to read NuGet configuration. The system `npm` shim could not find its installation; direct Node syntax/test entry points were used. An intermediate build attempted while tests held DLLs encountered file locks; the subsequent sequential build passed. Initial test failures concerned added starter counts, pre-existing-guidance collision fixtures and test stubs parsing the old unquoted evidence layout; assertions were adapted without removing their substantive checks.

The complete `tools/build-release.ps1` entry point requires a clean Git tree and archives `HEAD`. It was not run, because this requested change must remain uncommitted. Its build, test, dependency audit, package and packaged-tool checks were run separately. No clean-source release archive, commit, push, deployment or governed approval was created.

## HC acceptance mapping

| ID | Implementation | Verification and boundary |
| --- | --- | --- |
| HC-01 | Shared standard; eight pack files; host routing; `HumanReadableContentPolicy`; inventory above | Native guidance validation, starter link tests, actual service-request assertions. Live provider instruction loading not tested. |
| HC-02 | Technical/UI/onboarding/feature/approval/recovery renderers | State/action tests and independent reader answers; consequences and next actions visible. |
| HC-03 | Existing action IDs, CLI contracts, service gates and approval handlers retained | Agent/host/domain and extension regressions, permission-denial and approval-boundary tests. |
| HC-04 | Protected-fact policy, uncertainty guidance and conditional fixtures | Source-aware review and concrete regression checks; no claim of general semantic equivalence. |
| HC-05 | Distinct saved/reviewed/approved, preview/current/stale and not-run/failure messages | Partial-save, empty findings, stale review, preview and approval tests plus reader check. |
| HC-06 | BRD hidden evidence and source/version contracts retained | Existing BRD/agent evidence tests; policy revision refresh leaves canonical BRD bytes unchanged. |
| HC-07 | Checked-state message mapping and honest unknown fallbacks | Offline fixture/stub tests; no model or network for routine UI text. |
| HC-08 | Bounded overview, source action, sampled-input and provenance explanation | BRD summary fallback/cache tests and production source-form tests. |
| HC-09 | Bound embedded starter, per-template versions and normal collision ownership | Fresh/repeat/upgrade/custom-root/backend/editor/human-edit tests and packaged CLI smoke. |
| HC-10 | Wrapping, focus styles, visible findings/disabled reasons, footer in flow | 320px/200% approximation, focus/name/overflow checks and inspected PNGs. Live VS Code and screen reader follow-up remains manual. |
| HC-11 | Five source-backed scenarios and context-limited independent reader | Actual separate model reviewer, two passes plus a final UI clarification check; source-backed comparison. No human results claimed. |
| HC-12 | Native validation, .NET/Node regressions, package audit and packaging | Tests and other native checks pass. Strict graph validation retains the exact baseline identity warning; clean-tree release archive not run. See the final run record. |

## Final run record

The full solution run covered 794 tests and initially failed 11 architecture tests because their model stub still parsed the former plaintext evidence layout. Updating the stub to deserialize the evidence boundary retained all relationship/ownership assertions. The final architecture rerun passed 52/52. Added policy-cache coverage passed with the host suite (15/15, one new test) and delivery suite (70/70). Together with the other 23 passing projects, final coverage is 795 passing tests. Exact focused commands:

```text
dotnet test tests/Cis.Modules.SolutionDesign.Tests/Cis.Modules.SolutionDesign.Tests.csproj -c Release --no-build --no-restore
dotnet test tests/Cis.Host.Tests/Cis.Host.Tests.csproj -c Release --no-restore -p:BuildProjectReferences=false
dotnet test tests/Cis.Modules.Delivery.Tests/Cis.Modules.Delivery.Tests.csproj -c Release --no-restore -p:BuildProjectReferences=false
```

The system skill creator's `quick_validate.py` passed for each new skill using an existing local PyYAML dependency. `git diff --check` passed. A built CLI package was installed into `artifacts/readable-content/tool-final` using only the local package source and a fresh package cache. After the final editorial corrections, the final nupkg was unpacked under `artifacts/readable-content/` and its `tools/net10.0/any/cis.dll` was executed with `dotnet` using the same successful smoke commands. Both runs used a disposable repository as the current directory. The final embedded-resource changes also passed the abstractions (48/48) and repository (115/115) suites again.

Final strict graph validation reports `freshness=fresh`, zero errors and one warning, exit 5: `CIS-GRAPH-VALIDATE-IDENTITY-001`, 86 symbol/test nodes lack a confirmed component identity. An untouched archive of HEAD `8273a40478a4e186e2408175f4863abe80aba656`, built and strictly validated with the same CLI, reproduces the warning. A read-only SQLite comparison of every affected node key found identical sets: 86 baseline, 86 current, none introduced or removed. This pre-existing identity debt is not a passing strict check and was not suppressed or changed as part of content work. The information-only permissions-dictionary placeholder diagnostic is also present in both builds.

An interrupted intermediate graph build left a SQLite rollback journal that prevented read-only validation. The derived database and journal were preserved together in a local backup; a fresh native graph build restored readability. No canonical records or approval data were changed. Exact build/validation outputs are in `.codex-tmp/content-graph-final-build.log`, `.codex-tmp/content-graph-final-validate.log` and the corresponding `content-graph-baseline-*` logs. `artifacts/readable-content/verification.json` records final counts, package checksums and the baseline node-set comparison. These local outputs are excluded from graph inputs so recording the final command outcome does not invalidate its own evidence.

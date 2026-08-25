---
title: "CIS Command Manual"
type: navigation
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:root
---

# CIS command manual

- [`cis verify finalize`](cis_verify_finalize.md) — complete final lifecycle tasks, close, recapture, and record human acceptance.

CIS uses the command form:

```text
cis <module> <command> [options]
```

Each loaded module owns one top-level command group. The following leaf commands are currently available.

| Command | Purpose |
| --- | --- |
| [`cis ai providers`](cis_ai_providers.md) | Governed AI routing command. |
| [`cis ai models`](cis_ai_models.md) | Governed AI routing command. |
| [`cis ai routes`](cis_ai_routes.md) | Governed AI routing command. |
| [`cis ai usage`](cis_ai_usage.md) | Governed AI routing command. |
| [`cis ai evaluate`](cis_ai_evaluate.md) | Governed AI routing command. |
| [`cis ai cache status`](cis_ai_cache_status.md) | Governed AI routing command. |
| [`cis generate templates`](cis_generate_templates.md) | Deterministic generation command. |
| [`cis generate describe`](cis_generate_describe.md) | Deterministic generation command. |
| [`cis generate validate`](cis_generate_validate.md) | Deterministic generation command. |
| [`cis generate render`](cis_generate_render.md) | Deterministic generation command. |
| [`cis workflow list`](cis_workflow_list.md) | Deterministic workflow command. |
| [`cis workflow describe`](cis_workflow_describe.md) | Deterministic workflow command. |
| [`cis workflow run`](cis_workflow_run.md) | Deterministic workflow command. |
| [`cis workflow status`](cis_workflow_status.md) | Deterministic workflow command. |
| [`cis workflow log`](cis_workflow_log.md) | Deterministic workflow command. |
| [`cis workflow summarise`](cis_workflow_summarise.md) | Deterministic workflow command. |
| [`cis agent providers`](cis_agent_providers.md) | Portable agent-envelope command. |
| [`cis agent prepare`](cis_agent_prepare.md) | Portable agent-envelope command. |
| [`cis agent import-result`](cis_agent_import_result.md) | Portable agent-envelope command. |
| [`cis agent status`](cis_agent_status.md) | Portable agent-envelope command. |
| [`cis verify diff`](cis_verify_diff.md) | Verification and acceptance command. |
| [`cis verify compare`](cis_verify_compare.md) | Verification and acceptance command. |
| [`cis verify validate`](cis_verify_validate.md) | Verification and acceptance command. |
| [`cis verify evidence`](cis_verify_evidence.md) | Verification and acceptance command. |
| [`cis verify accept`](cis_verify_accept.md) | Verification and acceptance command. |
| [`cis diagnostics sources`](cis_diagnostics_sources.md) | Bounded diagnostics command. |
| [`cis diagnostics summary`](cis_diagnostics_summary.md) | Bounded diagnostics command. |
| [`cis diagnostics events`](cis_diagnostics_events.md) | Bounded diagnostics command. |
| [`cis diagnostics tail`](cis_diagnostics_tail.md) | Bounded diagnostics command. |
| [`cis diagnostics analyse`](cis_diagnostics_analyse.md) | Bounded diagnostics command. |
| [`cis learn collect`](cis_learn_collect.md) | Governed learning command. |
| [`cis learn propose`](cis_learn_propose.md) | Governed learning command. |
| [`cis learn review`](cis_learn_review.md) | Governed learning command. |
| [`cis learn apply`](cis_learn_apply.md) | Governed learning command. |
| [`cis learn history`](cis_learn_history.md) | Governed learning command. |
| [`cis host modules`](cis_host_modules.md) | List the modules explicitly loaded by the host. |
| [`cis repo init`](cis_repo_init.md) | Classify a repository and initialize or reconcile its CIS documentation workspace. |
| [`cis repo import`](cis_repo_import.md) | Initialize and register several existing repositories in one CIS workspace. |
| [`cis repo list`](cis_repo_list.md) | Validate and list repositories registered in a CIS workspace. |
| [`cis repo doctor`](cis_repo_doctor.md) | Inspect repository readiness, detect Ollama, and report evidence-backed suggested fixes. |
| [`cis skills inventory`](cis_skills_inventory.md) | List portable repository skills and detect structural errors. |
| [`cis skills validate`](cis_skills_validate.md) | Validate skill names, metadata, bodies, duplicates, and local resource links. |
| [`cis skills import`](cis_skills_import.md) | Import validated skill bundles from local paths, ZIP archives, or GitHub repositories. |
| [`cis skills audit`](cis_skills_audit.md) | Isolate duplicate, overlapping, and conflicting skills with local-first model review and optional quarantine. |
| [`cis workspace init`](cis_workspace_init.md) | Initialize the canonical documentation authority for a multi-repository workspace. |
| [`cis ai status`](cis_ai_status.md) | Report local and explicitly configured remote model providers without invoking generation. |
| [`cis api discover`](cis_api_discover.md) | Correlate source, API dictionary rows, and OpenAPI into normalized local state. |
| [`cis api inventory`](cis_api_inventory.md) | Read and filter the normalized API inventory without rescanning. |
| [`cis api validate`](cis_api_validate.md) | Enforce API governance and cross-artifact integrity. |
| [`cis api diff`](cis_api_diff.md) | Compare every supported OpenAPI baseline with the current contract using forward-transitive compatibility. |
| [`cis tracker plan`](cis_tracker_plan.md) | Preview external issue creates, updates, and conflicts without writing. |
| [`cis tracker push`](cis_tracker_push.md) | Project non-conflicting canonical tasks into configured external trackers. |
| [`cis tracker pull`](cis_tracker_pull.md) | Detect and persist remote drift without rewriting canonical tasks. |
| [`cis tracker status`](cis_tracker_status.md) | Report tracker providers, durable mappings, and conflicts. |
| [`cis tracker resolve`](cis_tracker_resolve.md) | Record an explicit human conflict disposition. |
| [`cis index build`](cis_index_build.md) | Build incremental, non-authoritative per-file routing cards. |
| [`cis index status`](cis_index_status.md) | Report file-card coverage and content-hash freshness without invoking a model. |
| [`cis index find`](cis_index_find.md) | Route task terms to cached file paths and short summaries. |
| [`cis feedback summary`](cis_feedback_summary.md) | Aggregate local tool outcomes and possible token savings. |
| [`cis feedback usage`](cis_feedback_usage.md) | List recent sanitized per-invocation usage evidence. |
| [`cis feedback opportunities`](cis_feedback_opportunities.md) | Find repeated failures and compact-output opportunities. |
| [`cis graph build`](cis_graph_build.md) | Build the deterministic disposable local context graph. |
| [`cis graph validate`](cis_graph_validate.md) | Validate graph integrity, evidence, freshness, and local-artifact safety. |
| [`cis graph find`](cis_graph_find.md) | Find graph nodes by identity, type, facet, or bounded text. |
| [`cis graph related`](cis_graph_related.md) | Traverse an evidence-preserving bounded graph neighbourhood. |
| [`cis graph trace`](cis_graph_trace.md) | Find bounded deterministic shortest paths between exact nodes. |
| [`cis graph export`](cis_graph_export.md) | Export the validated graph as Markdown, JSON, JSONL, or Graphviz DOT. |
| [`cis context search`](cis_context_search.md) | Search graph-backed context by identity, type, facet, or bounded text. |
| [`cis context contract`](cis_context_contract.md) | Retrieve the immediate evidence-backed context for a governed contract. |
| [`cis context symbol`](cis_context_symbol.md) | Retrieve the immediate evidence-backed context for a symbol. |
| [`cis context references`](cis_context_references.md) | Retrieve documents and governed references connected to an exact target. |
| [`cis context callers`](cis_context_callers.md) | Retrieve explicit consumers and dependents connected to an exact target. |
| [`cis context tests-for`](cis_context_tests_for.md) | Retrieve tests connected by explicit verification evidence. |
| [`cis context pack`](cis_context_pack.md) | Create a deterministic, budget-bounded Markdown context pack. |
| [`cis brd discover`](cis_brd_discover.md) | Find possible BRD evidence without claiming currency. |
| [`cis brd init`](cis_brd_init.md) | Create or reconcile the canonical review-required BRD. |
| [`cis brd status`](cis_brd_status.md) | Report effective BRD lifecycle and drift state. |
| [`cis brd validate`](cis_brd_validate.md) | Validate BRD content, evidence assessment, and baselines. |
| [`cis brd approve`](cis_brd_approve.md) | Record explicit human approval of a valid BRD. |
| [`cis brd backlog build`](cis_brd_backlog_build.md) | Decompose the Active BRD into traceable high-level product outcomes. |
| [`cis brd backlog validate`](cis_brd_backlog_validate.md) | Validate high-level coverage, source currency, routing, and dependencies. |
| [`cis brd backlog status`](cis_brd_backlog_status.md) | Report high-level backlog lifecycle and source drift. |
| [`cis brd backlog approve`](cis_brd_backlog_approve.md) | Record explicit human approval before feature-specification preparation. |
| [`cis brd backlog start`](cis_brd_backlog_start.md) | Start and link a Draft specification for one dependency-ready high-level item. |
| [`cis brd feature validate`](cis_brd_feature_validate.md) | Validate feature structure, traceability, UI coverage, currency, and approval evidence. |
| [`cis brd feature status`](cis_brd_feature_status.md) | Report the effective lifecycle of a backlog-derived feature specification. |
| [`cis brd feature approve`](cis_brd_feature_approve.md) | Record explicit human approval of a complete, current feature specification. |
| [`cis technical-intent init`](cis_technical_intent_init.md) | Bind workspace technical direction to the Active BRD and participant graph baselines. |
| [`cis technical-intent validate`](cis_technical_intent_validate.md) | Validate technical-intent completeness, decisions, currency, and approval evidence. |
| [`cis technical-intent status`](cis_technical_intent_status.md) | Report effective technical-intent lifecycle and drift state. |
| [`cis technical-intent refresh`](cis_technical_intent_refresh.md) | Refresh unchanged BRD, technical-intent, and backlog baselines without duplicate approval. |
| [`cis technical-intent approve`](cis_technical_intent_approve.md) | Record explicit human approval of valid, current technical direction. |
| [`cis docs inventory`](cis_docs_inventory.md) | Inventory Markdown beneath the configured documentation root. |
| [`cis docs validate`](cis_docs_validate.md) | Validate the documentation catalog and its Markdown files. |
| [`cis standards inventory`](cis_standards_inventory.md) | List canonical standards with target, stack, and lifecycle filters. |
| [`cis standards applicable`](cis_standards_applicable.md) | Resolve active standards for affected targets and technology stacks. |
| [`cis standards validate`](cis_standards_validate.md) | Validate standard metadata, stable rules, and conformance coverage. |
| [`cis standards conformance`](cis_standards_conformance.md) | Inspect rule enforcement mappings and governance gaps. |
| [`cis standards import`](cis_standards_import.md) | Preview and admit validated standards from local paths, ZIPs, or GitHub URLs. |
| [`cis standards audit`](cis_standards_audit.md) | Find duplicate, overlapping, or conflicting standards and optionally quarantine them. |
| [`cis standards patterns`](cis_standards_patterns.md) | Inventory and validate known compiler-graph patterns and repository extensions. |
| [`cis standards infer`](cis_standards_infer.md) | Match ready patterns against a fresh compiler graph and emit non-canonical candidates. |
| [`cis change create`](cis_change_create.md) | Create a catalogued change dossier against an exact baseline. |
| [`cis change rebaseline`](cis_change_rebaseline.md) | Adopt a genuine pre-impact baseline change with actor, rationale, and audit history. |
| [`cis change list`](cis_change_list.md) | List repository-owned change dossiers. |
| [`cis change show`](cis_change_show.md) | Show a change proposal and baseline. |
| [`cis change status`](cis_change_status.md) | Report change lifecycle status. |
| [`cis change close`](cis_change_close.md) | Record explicit change closure. |
| [`cis impact analyse`](cis_impact_analyse.md) | Discover deterministic graph-backed impact proposals. |
| [`cis impact findings`](cis_impact_findings.md) | List findings, evidence, and dispositions. |
| [`cis impact accept`](cis_impact_accept.md) | Accept an impact with a human review reason. |
| [`cis impact reject`](cis_impact_reject.md) | Reject an impact with a human review reason. |
| [`cis impact defer`](cis_impact_defer.md) | Defer an impact while keeping it visible as a gap. |
| [`cis impact completeness`](cis_impact_completeness.md) | Assess review completeness and planning readiness. |
| [`cis decision list`](cis_decision_list.md) | List change-local decisions, evidence, gates, and state. |
| [`cis decision create`](cis_decision_create.md) | Record a question, options, evidence, and approval gate. |
| [`cis decision resolve`](cis_decision_resolve.md) | Record the human-selected option and rationale. |
| [`cis decision defer`](cis_decision_defer.md) | Defer a decision while preserving its gate effect. |
| [`cis decision promote`](cis_decision_promote.md) | Promote a resolved durable decision into a catalogued ADR. |
| [`cis plan build`](cis_plan_build.md) | Build bounded work from accepted impacts. |
| [`cis plan import-spec`](cis_plan_import_spec.md) | Import a feature specification and generate a catalogued, complexity-bounded issue pack with design and verification gates. |
| [`cis plan derive`](cis_plan_derive.md) | Atomically carry current feature approval through eligible impacts and the exact generated plan. |
| [`cis plan show`](cis_plan_show.md) | Show the canonical dependency-aware plan. |
| [`cis plan validate`](cis_plan_validate.md) | Validate coverage, dependencies, decisions, acceptance, and tests. |
| [`cis plan approve`](cis_plan_approve.md) | Record explicit human approval of a valid plan. |
| [`cis plan status`](cis_plan_status.md) | Report plan lifecycle and readiness. |
| [`cis plan capability status`](cis_plan_capability_status.md) | Show canonical extension capability selections and unresolved provider conflicts. |
| [`cis plan capability select`](cis_plan_capability_select.md) | Record human-approved provider selection and compatible extension replacement. |
| [`cis plan task transition`](cis_plan_task_transition.md) | Move a generated task through its audited lifecycle while enforcing gates. |
| [`cis plan task migrate-type`](cis_plan_task_migrate_type.md) | Migrate an extension task to the selected compatible provider while preserving human evidence. |
| [`cis design templates`](cis_design_templates.md) | List reusable application-shell/component templates and possible token savings. |
| [`cis design scaffold`](cis_design_scaffold.md) | Scaffold one self-contained renderer from validated textual wireframes. |
| [`cis design wireframe-validate`](cis_design_wireframe_validate.md) | Validate classified screens, states, actions, paths, and coverage before human review. |
| [`cis design wireframe-approve`](cis_design_wireframe_approve.md) | Approve the exact textual screen, action, state, and path contract. |
| [`cis design wireframe-reject`](cis_design_wireframe_reject.md) | Reject textual wireframes with preserved digest and rationale. |
| [`cis design render`](cis_design_render.md) | Render the PNG pack and activate the global human-review pause. |
| [`cis design validate`](cis_design_validate.md) | Validate renderer, guideline, shell, provenance, and artifact evidence. |
| [`cis design reconcile`](cis_design_reconcile.md) | Carry current feature authority across an unchanged PNG refresh without duplicating approval. |
| [`cis design approve`](cis_design_approve.md) | Approve exact renderer/manifest hashes and release the global design gate. |
| [`cis design reject`](cis_design_reject.md) | Reject the pack, preserve hashes, remove PNGs, and retain the pause. |
| [`cis design status`](cis_design_status.md) | Show the current design gate, approval, renderer, and artifacts. |

## Common conventions

Commands that return structured results support `--format human`, `--format json`, and `--format agent`.

- `human` is the default and is intended for interactive use.
- `json` emits a structured result for programs.
- `agent` emits stable, line-oriented `key=value` records for agent and shell workflows.

Diagnostics such as an unsupported format are written to standard error. Every command provides `-?`, `-h`, and `--help`; command-specific exit codes are documented on its page.

## Repository context

Repository-aware commands accept `--repo` and default it to the current directory. After initialization, `.cis/repository.yml` identifies the repository and its documentation root, so documentation commands do not need a separate root argument.

Multi-repository commands accept `--workspace`. The canonical `.cis/workspace.yml`
registry records initialized repositories by stable ID and path. Workspace graph
operations retain one independent graph generation per repository; context packs may
resolve registered IDs and federate those graphs at query time.

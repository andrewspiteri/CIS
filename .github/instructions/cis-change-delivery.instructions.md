---
applyTo: "docs/changes/**"
---

# CIS change-delivery records

- Treat proposal, impact, decision, and plan Markdown as canonical review records.
- Use `cis change`, `cis impact`, `cis decision`, and `cis plan` commands for managed lifecycle and table changes.
- For a current approved CIS feature, prefer `cis plan derive`; it carries the exact existing authority through eligible deterministic impacts and the generated plan atomically. Use `cis plan import-spec` and explicit impact/plan review only for reported exceptions or sources without reusable approval authority. Never copy the feature into a competing source of truth.
- Treat `plan.md` as the dependency ledger and each `agent-tasks/WORK-NNN.md` file as the executable task contract.
- Require complexity, bounded required changes, exclusions, evidence, dependencies, acceptance checklists, validation, completion evidence, and deferral rationale in every task.
- For UI-bearing scope, validate `wireframes.md`, reuse `cis design templates`, and render every screen inside the governed application shell. By default the rendered-design decision approves the exact wireframe digest and visual pack together; a separate wireframe approval is optional.
- Classify every frontend requirement, screen, design artifact, and frontend task as `public`, `customer`, or `backoffice`; do not merge affected types into an unclassified UI task.
- Every unauthenticated endpoint must traverse a governed cache. Its route/controller/handler must not directly access a database, database context/client, query provider, or repository, including on cache miss; apply and verify `PUBLIC-ENDPOINT-CACHE` obligations.
- A successful `cis design render` sets the global design gate to `PausedForReview`; stop all non-review work until a human runs `cis design approve` or `cis design reject` with reviewer identity and rationale, or until `cis design reconcile` deterministically carries current feature authority across an unchanged PNG manifest. Reconciliation is not a new human approval and must fail closed on any visual, feature, plan, or authority mismatch.
- Rejection preserves renderer/PNG hashes and rationale, removes rejected PNGs, and permits only wireframe/design revision while the pause remains active.
- Record exact cross-task validation and assurance evidence in `verification.md`.
- Treat `test-cases.md` and `test-cases.csv` as synchronized derived feature artifacts. Review their requirement coverage before implementation. Put every stable `TC-*` identity in its corresponding automated test name, framework metadata, or adjacent traceability annotation, then rerun `cis plan import-spec` or `cis plan derive` after test or source changes to refresh exact cross-repository mappings. Planning may retain `Pending`; final verification must not. Keep execution results in `verification.md`.
- Resolve extension provider conflicts through the canonical `docs/references/task-type-capability-selections.md`; never infer selection from provider load order. Selection does not migrate existing tasks.
- Use `cis plan task migrate-type` only for a repository-selected compatible extension replacement and preserve stable task identity, evidence, deferrals, external links, and migration history.
- Final Delivery Sweep owns deterministic readiness; Coordination alone owns final human feature acceptance after all child dispositions resolve.
- Capture a workspace-aware `cis verify diff` after evidence stabilizes. Never accept a legacy, empty, digest-invalid, or stale snapshot; use `cis verify finalize` for explicitly authorized final acceptance.
- Do not hand-edit stable IDs, exact baselines, finding disposition, decision state, plan approval, or audit events.
- Deterministic analysis and AI may propose findings, questions, options, and work; they may not invent human disposition, resolution, approval, or closure authority. `cis plan derive` may reuse tool-confirmed current feature authority with exact provenance and must stop on uncertainty.
- Preserve rejected and deferred records with rationale rather than deleting them.
- Rebuild and validate the graph after canonical decisions or promoted ADRs change.
- Validate the plan before implementation and run strict documentation validation after canonical updates.

---
title: "cis plan import-spec"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-23"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-plan-import-spec
---

# `cis plan import-spec`

In a workspace authority, import is blocked unless `cis technical-intent status` reports
Active and current.

Imports a canonical feature specification into an existing reviewed change and builds
a PARR-style, complexity-bounded issue pack rather than only a summary table.

For a current CIS-managed feature with explicit human approval, prefer
`cis plan derive`. It atomically adopts eligible deterministic impacts, imports and
validates this same task pack, and records the existing feature authority against the
exact generated plan. Use `import-spec` directly when no compatible approval authority
exists or when an exceptional finding needs separate human disposition.

```text
cis plan import-spec <change-id> --file <repo-relative-spec.md> [--repo <path>] [--format <human|json|agent>]
```

The Markdown source must be inside the repository and outside `.cis/local`. CIS accepts
its native `type: feature-specification` contract and PARR-compatible
`type: specification` documents with a feature title. Intake supports either:

- an `ID`, optional `Surface`, `Requirement`, and `Acceptance criteria` table; or
- a rich narrative specification with a numbered Goals section.

Narrative goals receive stable derived `GOAL-NNN` IDs. Front matter `targets`, `stack`,
and `references` are preserved as task-pack provenance. Recommended rich sections cover
non-goals, workflows and states, model/data/migrations, API and permissions, UX,
integrations, product-specific extensions such as search/retrieval,
lifecycle/carry-forward, security, and testing.

IDs must be unique and every row must replace TODO and placeholder content. CIS keeps
the source Markdown canonical and records its repository-relative path and SHA-256
digest in `plan.md`; it does not create a competing copy.

Authoring markers are the case-sensitive standalone tokens `TODO` and `TBD`, plus the
phrase `to be completed`; product names and repository IDs such as `Friends Todo` and
`todo-api` are ordinary content. Structured `Frontend type: not-applicable` requirements
and an explicit no-product-UI UX section do not create wireframe, design, or frontend
tasks. Negative or non-goal mentions of backfill and rollout do not activate those task
types without a corresponding functional requirement. Data, schema migration, API,
integration, lifecycle, and other optional workstreams likewise require positive
structured requirement evidence; words in exclusions or descriptive section headings
do not make a task applicable. Credential names such as `API key` are not themselves
API-contract evidence.

The command requires planning-ready, human-reviewed impact findings. It writes the
dependency ledger to `plan.md` and one durable Markdown task per workstream under
`agent-tasks/`. It also generates `test-cases.md` for human review and
`test-cases.csv` for import into TestRail or another test-management system.
Workstreams are created only when the specification requires them:

- parent coordination and scope guard;
- wireframes followed by visual/interaction design and one exact combined review;
- documentation, security/permissions, data, schema migration, and backfill;
- API contracts, backend behavior, approved frontend implementation, and integrations;
- infrastructure, observability, lifecycle, and rollout where signalled;
- deterministic verification and independent assurance; and
- final delivery reconciliation and handoff.

`plan.md` is the human approval surface. It presents a short approval summary and a
compact task table containing only the task, area, complexity, dependencies,
requirements, repositories, and status. Provider identifiers, impact provenance,
policy clauses, validation recipes, and execution evidence are retained in a hidden
canonical execution manifest and the linked agent task documents; reviewers do not
need to approve those mechanics line by line. JSON and agent output formats continue
to expose the complete structured task contract for automation.

Documentation, security, data, schema/backfill, API-contract, backend, observability,
and rollout tasks retain assigned requirement IDs and required-change traceability but
use provider-owned acceptance criteria. Cross-surface outcome acceptance remains with
frontend/integration/verification/assurance/delivery tasks, preventing an API task from
being blocked by later UI or infrastructure work.

Every UI-bearing requirement should include a `Frontend type` column with exactly
`public`, `customer`, or `backoffice`. CIS creates a separate matched wireframe,
design, and frontend implementation task for each affected type. Legacy specifications
without the column are inferred from explicit public/backoffice language and otherwise
treated as customer-facing. Each classification-specific task body contains only its
assigned requirement rows; the complete UX section remains available through the
canonical feature-specification reference without leaking another classification's
screens into the task.

When workspace target manifests provide repository roles, CIS routes UI tasks to
frontend repositories, backend/data work to backend or database repositories,
contracts to frontend and backend consumers/producers, and infrastructure work to
infrastructure repositories. Cross-cutting coordination, documentation, security,
integration, observability, verification, assurance, and delivery remain workspace-wide.
Missing role evidence falls back to the declared feature targets.

When the specification identifies an unauthenticated public application endpoint, CIS
also creates or activates Security, API contract, Backend, Observability, Verification,
and Independent assurance tasks containing `PUBLIC-ENDPOINT-CACHE`. These tasks require
a governed cache response path and prohibit direct database/repository access from the
route/controller/handler, including on cache miss. The detailed policy is seeded as
`specs/public-endpoint-caching-policy-spec.md`. Pre-authentication identity protocol
routes such as OAuth callbacks, token exchanges, and session creation are not public
application endpoints solely because they are unauthenticated; they use `no-store` and
retain the transport-to-application dependency boundary.

Search/projection is not a CIS core task type. A product module may register it through
`ICisTaskTypeProvider`; the imported source remains the evidence for applicability.

Every task document contains its objective, required changes extracted from relevant
specification sections, constraints and non-goals, accepted graph evidence, dependencies,
approval gates, acceptance checklist, targeted validation, completion-evidence table,
and explicit deferral/residual-risk section. Task documents are catalogued canonical
records. `design.md` records screen artifacts and human approval; `verification.md`
aggregates exact commands, artifacts, results, blockers, and assurance evidence.

Manual test cases are deterministic projections of the imported functional
requirements. Each requirement receives one stable `TC-<requirement-id>-001` case with
title, section, priority, type, preconditions, numbered steps, expected result,
requirement reference, frontend type, and automation status. The Markdown file records
the feature path/digest, case count, and exact CSV SHA-256. The CSV uses one row per
case with portable, mappable headers and RFC-style quoting; formula-like leading
characters are neutralized for spreadsheet-oriented importers. Re-import regenerates
both files together. They define cases only—execution results remain in
`verification.md`.

During implementation, automated tests must contain the exact stable `TC-*` identity
in the test name, framework metadata, or an adjacent traceability annotation. Import
scans recognized test sources in every registered workspace repository, records exact
`repository::path:line` references, and marks discovered cases `Automated`; undiscovered
cases remain `Pending`. Vendor and generated trees such as `node_modules`, build output,
coverage, and `.cis/local/` are excluded. Rerun the unchanged import after adding or
moving tests to refresh traceability. This derived-only refresh preserves an existing
plan approval when the canonical feature digest is unchanged.

Each item records its stable task-type key/version and is classified `low`, `medium`,
or `high`. A high item is stored only as a `decomposed` parent and must have at least
two bounded low- or medium-complexity children before validation can pass.

For frontend work, `coordination -> validated wireframes -> rendered design approval ->
every downstream task` is required. A rendered design sets a global pause: no
implementation, verification, or delivery task may start until a human approves the
exact wireframe digest, renderer, and PNG manifest together. Teams may use the separate
wireframe approval command for an earlier review, but it is not a mandatory second stop.

Repeating the command against unchanged source and impact state returns `unchanged`.
This includes byte-identical manual-test Markdown and CSV projections.
When a revised specification no longer selects a generated task, CIS moves the entire
task document under `agent-tasks/retired/`, sets its document and task lifecycle to
archived/retired, changes its catalog route to historical status, appends retirement
history and an audit event, and preserves all human evidence, approvals, deferrals,
and external synchronization records. A later re-import that selects the task again
restores the record, preserves its evidence/history, and resets lifecycle status so
old completion cannot silently approve renewed work.

Plan validation and task transitions compare the current canonical feature file with
the digest recorded in `plan.md`. Source drift blocks execution even when the prior plan
still says `Approved`. Re-importing a revised specification from the same path regenerates
the task pack, records `plan-approval-invalidated`, and demotes the plan to `Draft` for
renewed human review. Importing a different source path remains blocked for an approved
plan.

For an active task, re-import preserves checked checklist items only when the complete
normalized criterion text is unchanged. Revised or removed criteria do not inherit old
completion. An approved plan may be regenerated only from the exact same source path and
digest without discarding task lifecycle, human evidence, or checklist state. A revised
same-path digest is regenerated only as Draft; unchanged approved input remains Approved.
Plan approval remains explicit human authority. `cis plan approve` records new authority;
`cis plan derive` reuses current feature authority with exact digest provenance and does
not invent or infer approval.

Exit `0` means the import and resulting plan are valid. Exit `5` means a plan was
created but an approval gate remains. Exit `2` means the source, change, or request is
invalid.

# Windows .NET/C# project trial

Use isolated candidate **0.3.0-engineering.20261009.34**, now qualified for the bounded Windows .NET/C# trial.
The [readiness record](engineering-defaults-trial-readiness.md) identifies the tested package and limitations.
This guide does not approve your BRD, choose unresolved business decisions or authorize later product phases.

## 1. Select the candidate and initialize a short checkout

Use a short project path such as `C:/work/cis-trial` to leave room for isolated worktrees and build outputs.
You need Windows, .NET 10 SDK, Git and PowerShell 7. The optional reference business/browser checks additionally
need a working local Docker engine and the pinned browser/scanner prerequisites in the
[example README](../examples/dotnet-engineering/README.md). Restore project dependencies from their pinned manifests.
Claude or Codex must be installed and authenticated before agent execution; native provider diagnosis is available
through `agent provider diagnose`. No API key is required by this guide.

From the CIS candidate worktree:

```powershell
$cis = (Resolve-Path '.cis/local/engineering-tool-rehearsal-34/cis.exe').Path
& $cis --version
$project = 'C:/work/cis-trial'
New-Item -ItemType Directory -Path $project -Force | Out-Null
& $cis workspace init --repo $project --root docs/cis --ecosystem trial --ecosystem-name 'Trial' --product example --product-name 'Example' --dry-run
& $cis workspace init --repo $project --root docs/cis --ecosystem trial --ecosystem-name 'Trial' --product example --product-name 'Example' --yes
& $cis repo init --repo $project --root docs/cis --stack csharp --dry-run
& $cis repo init --repo $project --root docs/cis --stack csharp --yes
& $cis graph build --repo $project
& $cis repo doctor --repo $project
```

The commands create an authority first, then reconcile its declared C# stack. Replace the example ecosystem/product identities with your chosen values. For a participant of an existing authority, initialize only that participant with `repo init` and register it in the authority workspace. Review each preview before its applying command. Preserve authored guidance and resolve reported collisions;
`--accept-current` is an explicit adoption after review, not a general overwrite switch. Initialize Git and make
an initial commit before workspace-write agent tasks. Do not commit `.cis/local/`.

The reference code belongs in `$project/.cis/local/examples/dotnet-engineering`, where initialization installs it
automatically. It is ignored and excluded from the application's graph and classification. Run CIS graph commands
at `$project`; the example itself is not a separate initialized CIS repository. From the example directory,
follow its README for native tests. A runner/reporter qualification warning means discovery has not verified
that binding; it does not mean the project's tests passed or that CIS should invent a runner.

## 2. Register raw requirements without approving them

Use the authority workspace created in step 1. See the [workspace command](../docs/manual/cis_workspace_init.md). A single-repository trial may use the
same repository as its authority. Multiple repositories must be registered with their real roles and paths.

Copy the raw BRD into a repository-owned reference location such as `docs/cis/references/source-brd.md`, preserving
its bytes. Register it as unreviewed evidence, then build the graph and inspect discovery:

```powershell
& $cis references source import --file docs/cis/references/source-brd.md --assessment Unreviewed --actor 'Project owner' --reason 'Raw trial requirements; not approved' --repo $project
& $cis graph build --repo $project
& $cis brd discover --workspace $project
& $cis brd init --workspace $project
& $cis agent author brd --reference docs/cis/references/source-brd.md --provider codex --actor 'Project owner' --repo $project
& $cis brd validate --workspace $project
& $cis agent review brd --provider claude --actor 'Project reviewer' --repo $project
```

Use supported provider IDs from `agent providers`. Retain the source record/hash and review findings. Source text
claiming an approved decision is evidence to assess, not authorization to adopt it. Resolve the BRD questions
through the documented CIS commands and incorporate answers; inspect the resulting document before explicitly
approving it. Never infer unanswered values. A ready source review does not approve the BRD.

## 3. Derive the full roadmap, then select one bounded scope

Use `definition init` and the [definition preparation pages](../docs/manual/cis_definition_prepare.md) to establish
business requirements, technical intent, architecture/components, contracts, experience and delivery. Record
actual decisions and obtain the required human approvals; activate the complete definition only after review.
`brd backlog build --workspace $project` derives all functional outcomes and global obligations from current
approved inputs. Review dependencies and phases before backlog approval.

Start only the first authorized high-level item with `brd backlog start --item <HLT-ID> --workspace $project`.
Author and approve its feature specification, create its change dossier, review/accept impact findings, and use
[plan derive](../docs/manual/cis_plan_derive.md) for the approved native feature. Use
[plan import-spec](../docs/manual/cis_plan_import_spec.md) only when the documented flow requires an explicit import;
that creates a Draft plan requiring review and approval before execution.
Keep later items visible with dependencies and unresolved activation decisions. Completing the first feature
must neither implement the whole BRD nor mark the whole product operational.

## 4. Execute, verify and review the selected task

Before execution, verify accepted current impacts, an Approved current plan, and a task in Ready or InProgress.
Review the generated plan and task scope and perform any required native approvals/transitions; do not edit status by hand. Use the repository's native framework and declared
workflows; the reference harness supplies examples, not a replacement test framework. Inspect `workflow list`
and `workflow describe <name>` before `workflow run <name> --repo $project`.

```powershell
& $cis agent run <change-id> <task-id> --provider codex --mode implement --permission workspace-write --actor 'Project implementer' --repo $project
& $cis agent show <run-id> --summary --repo $project
& $cis plan task completion-context <change-id> <task-id> --repo $project --format json
```

Replace placeholders with IDs actually returned by CIS. For Claude build commands, use optional per-attempt
`--allow-command` entries for the reviewed native invocations; see [command permissions](../docs/manual/cis_agent_run.md#explicit-command-permissions).
A permission-required result is a failed attempt, not a pending approval dialog. A provider's success message is
not proof that commands ran: inspect native results and retained outputs.

Apply/import implementation evidence through the task's governed workflow, rerun applicable native checks against
the final source, then request a distinct assigned reviewer with `agent run ... --mode review --permission read-only`.
Inspect findings and callers, correct blocking issues, and repeat affected checks and review. Record passed, failed,
missing, stale, skipped and justified inapplicable gates distinctly. Follow the
[completion-context contract](../docs/manual/cis_plan_task_completion_context.md) to retain native artifact hashes,
exact requirements, source/policy identity and distinct run IDs at its returned receipt path. Do not mark template
gates passed, fabricate a baseline, or hand-edit approval hashes.

## 5. Close the iteration and check project growth

After the task's changes settle, rebuild its graph, preview and reconcile `repo init`, update applicable CIS
standards/skills/dependencies while preserving customizations, and reassess all required gates. Rebuild and rerun
affected evidence after any resulting changes. The assigned reviewer double-checks final alignment and context.
Then request the native task transition:

```powershell
& $cis plan task transition <change-id> <task-id> --status Complete --actor 'Project reviewer' --reason 'Current required gates and review verified' --repo $project
```

A refusal is useful evidence: inspect the missing/stale/failed obligation and resolve it. Do not bypass completion
by editing status. At the change's final sweep, run its whole-change diff/comparison/validation and obtain explicit
human acceptance. For the standard final-sweep/coordination plan, use
[verify finalize](../docs/manual/cis_verify_finalize.md) to close and recapture the final bookkeeping consistently.
If using separate acceptance and closure commands, retain the accepted snapshot and refresh the derived diff
after those writes, then validate again; a closed status alone is not proof of current verification.

On the next task, add only its authorized component, then repeat graph build, initialization preview/reconciliation,
Doctor, required-gate assessment and reviewer context checks. New implementation must gain relevant standards and
skills even if the repository started empty. To test this detection deliberately, use a separate disposable empty
repository initialized without `--stack`, add a small .NET project, then repeat initialization and inspect its
classification and new guidance. Never count local reference examples as application adoption.

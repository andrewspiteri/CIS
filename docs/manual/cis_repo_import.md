---
title: "cis repo import"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-22"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-repo-import
---

# `cis repo import`

Initializes and registers between one and twenty existing repositories in a CIS
workspace. Import records repository locations; it does not copy, move, clone, or
execute source repositories.

## Synopsis

```text
cis repo import --source <path> [<path>...] --root <repository-relative-path>
  --participation <owned|dependency>
  --relationship <none|producer|consumer|bidirectional>
  [--workspace <path>] [--dry-run] [--yes] [--merge-review <hash>]
  [--guidance-mode <minimal|reconcile>]
  [--merge-edits <json-file>]
  [--merge-provider <provider> --merge-model <model> [--allow-remote-merge]]
  [--component <id>...] [--ecosystem <id>] [--product <id>]
  [--ecosystem-name <name>] [--product-name <name>]
  [--format <human|json|agent>]
```

## Workflow

The CLI defaults to minimal import. In-process callers of `RepositoryImportRequest`
retain the full starter mode for compatibility; they must set `MinimalImport: true`
to select minimal import and supply the reviewed preview hash when applying it.

The command resolves and deduplicates every source path, plans initialization for the
complete batch, and rejects the batch before mutation when any source is invalid or has
an initialization collision. It then merges the repositories into
`.cis/workspace.yml`. Registry paths are stored relative to the workspace when
possible.

For an existing standalone repository, set both `--workspace` and `--source` to that
repository. If no workspace configuration exists, the import requires explicit
`--ecosystem` and `--product` identities, initializes the existing source in place, and
registers it as the workspace `authority` in one transaction. This
is the preferred existing-repository onboarding flow; it does not copy or rewrite source
implementation files.

Run `cis workspace init` first when a separate documentation repository will own the
product's canonical documents. Other imports use role `participant`. `owned` imports are
implementation targets governed by this product. `dependency` imports are bounded read
context owned elsewhere and require a directional producer, consumer, or bidirectional
relationship. Importing the authority repository again never downgrades its role.

Without `--yes` and the reviewed `--merge-review` hash, a non-empty minimal apply plan returns a confirmation-required result and
does not change canonical files. After confirmation, every source is initialized using the selected documentation
root, then the workspace registry is atomically replaced. Repeating the same import is
idempotent and reports `unchanged`.

## Minimal import and gap report

The default `--guidance-mode minimal` preserves existing directives. It adds repository
configuration, a documentation catalogue and small navigation page, a scoped
`.github/instructions/cis-import.instructions.md`, and a short managed CIS section in
`AGENTS.md` and an existing Copilot entry point. It does not generate the full starter
library, migrate document authority, retire guidance, or invoke a model.

Missing CIS runtime profiles are proposed independently of guidance matching: local AI
routing, provider permissions, model qualification, source evidence and local artifact
retention. Existing profiles are preserved byte for byte and reported as unverified.
When supported test projects or declared package scripts are detected, import proposes
CIS test bindings using their existing harness, including .NET Playwright. Commands,
prerequisites and result paths require verification before execution. No synthetic
test runner is installed when detection finds no supported command. Security scanner
bindings remain an explicit setup item when absent; import does not replace adopted
scanners or CI gates with defaults.

A local requirement inventory inspects root entry points, conventional GitHub guidance
and discovered local references. Sixteen individual checks cover source evidence,
backend authorization, secrets, denied-access tests, scanner evidence, security
exceptions, test execution, browser behavior and data, architecture, contracts and
human approval. Each match requires specific evidence within one paragraph or list;
a topic name or filename alone cannot suppress the other requirements. Matching text
is preserved and reported as unverified. A proposed baseline fills each unmatched
requirement only where project policy is silent. Unreadable or omitted evidence defers
these additions. This bounded text check does not prove semantic equivalence or full
coverage, and executes no tests, scanners or CI gates.

Temporary, cache, generated-runtime, dependency and build directories are skipped
before traversal, including `.github/tmp`, `.github/temp`, `.github/copilot-runtime`,
`node_modules`, `bin`, `obj`, `test-results` and `playwright-report`. Explicit links into
these directories are also ignored. Authored instructions, skills, scripts and
workflows remain eligible evidence. Limits are 500 files, 256 KB per file and four
million characters; implicit references and other layouts may remain undiscovered.

JSON includes `assessments`, changed-file `previews`, and an input-bound review hash.
The report separates CIS setup from preserved guidance, scoped additions, unverified coverage,
routing follow-ups and possible overlaps. Multiple requirement matches are candidates for
later inspection, never permission to consolidate. Full source text remains in place.
After import, the report is saved to `.cis/local/import/report.json`, outside the
canonical documentation and normal tracked changes. Additional capability packs and
governed specifications remain deferred until a reviewed workflow needs them.
`repo doctor` reports missing, never-installed capability profiles as setup-pending
warnings after minimal import. Invalid existing files and missing previously managed
files remain errors. Direct capability validation still requires its own configuration;
registration does not establish testing, security or standards compliance.

VS Code opens **Review minimal CIS import** with the report and changed lines. Existing
entry-point text is available in expandable editable diffs. **Save guidance and import**
applies the reviewed plan. Cancelling leaves it unapplied. Changes to inventoried inputs
or the registration plan invalidate its hash. Optional `--merge-edits` carries explicit
entry-point edits; the CLI does not run a model during apply.

## Explicit full reconciliation

The remaining guidance-merge behavior applies only to `--guidance-mode reconcile`.
This opt-in mode creates the full starter set and can propose substantial changes.

An existing `AGENTS.md` receives a proposed merge instead of an unmanaged-file
collision. The marked section receives the current CIS guidance. With an explicitly
selected model, the dry run reviews every editable instruction against the new CIS
entry guidance and repository instructions. A second pass checks for missed repetitions,
numbered lookup sequences, conflicting workflow steps, and excessive deletions.
CIS takes precedence. Superseded routes are proposed for deletion, including their
supporting links, setup commands and examples when those serve only the replaced
workflow. The review must not retain an obsolete route by relabeling it as secondary,
supplementary or a fallback after CIS. Distinct project capabilities, domain rules,
contracts and security protections remain; a non-CIS name or path alone is not a
reason for removal. A mixed paragraph can receive a proposed replacement that keeps
only its still-applicable details. The second pass also checks replacement wording
for obsolete fallback routes and restores useful details removed in error.
An existing `.github/copilot-instructions.md` receives its own editable proposal in
the same review. CIS discovers non-CIS `.github/instructions/*.instructions.md`,
`.github/skills/*/SKILL.md`, `.github/agents/*.agent.md` and
`.github/prompts/*.prompt.md` independently of entry-point links. Up to 198 such files
receive editable proposals, with visible warnings for excluded paths or limits.
Their front-matter scoping is preserved. Generated CIS files are excluded; scripts,
workflows and canonical domain documents remain read-only. Each editable file gets two model passes.
Authorized remote reviews run up to four model requests concurrently. Larger review sets
batch up to eight complete non-entry-point files from one repository, bounded by 350
instruction blocks and 80,000 input characters. Entry points, large files and sensitive
files stay separate. Every file retains its own scope, block coverage and editable proposal;
duplicate removal cannot cite a surviving instruction in a different existing file.
Both passes for a batch remain sequential, with the same selected model and full instruction coverage. Local
reviews run one file at a time to avoid competing for model memory. Concurrency reduces
waiting between files; batching reduces repeated model calls and shared context but cannot guarantee a duration.
Other guidance locations and references outside this discovered set require separate review.

The review includes bounded evidence from direct repository-relative references and
named local skills and agents, including relative Markdown links, prioritizing routing scripts, governance skills and foundational
intent documents. `contextSources` lists up to 16 reference files per proposal, with
category limits; files over 2,000 characters contribute labelled excerpts and files
over 256 KB are skipped. Excerpts retain line labels and selected policy/routing lines.
Limits and unreadable or unsafe paths produce warnings. Paths cannot escape the
repository or traverse symbolic links, references are never executed, and suspected
credentials are excluded. This is not a recursive or complete repository guidance audit.

The model checks indirect routing behaviour shown by that evidence, preserves named
domain-maintenance obligations when retiring a workflow, and distinguishes old intent
documents as evidence pending CIS reconciliation and approval. Repeated retained rules
may cite an unchanged surviving instruction as their duplicate replacement. Formatting
cleanup renumbers surviving lists without breaking procedures around indented examples, and
collapses extra blank lines while preserving fenced examples and front matter.
Each suggested removal includes its original line range, reason, method, and exact
replacement instruction. These are review proposals, not canonical changes.
Unresolved dependencies and enforcement gaps appear as `guidanceReview.findings`.
CIS also checks local workflow/script text for references to removed commands; these
matches identify inspection candidates, not proof that a command executes. The bounded
automation inventory stays local and is included in the review hash. Limits are visible.
Import never silently disables or migrates a CI gate. It preserves another tool's distinct
usage ledger and flags claims that CIS feedback replaces that history.
JSON output includes
`fileMerges` with the existing and proposed content, and `mergeReviewHash` for the
complete set of input revisions. Review the changes before applying them with
`--yes`, `--merge-review <hash>`, and `--merge-edits <json-file>` containing the final
reviewed text. Changes to either editable file, generated guidance, or any included
reference or inspected automation file invalidate the review, including changes outside a displayed excerpt.
The model does not run again during apply. Incomplete or duplicated section markers
remain a collision.

The VS Code import action offers the available review models, including models from
the signed-in Codex CLI account. Remote selection explains which guidance will be sent
and lists both editable files and read-only reference inputs before authorization.
The extension accepts import responses up to 64 MiB per output stream, including the
initial dry-run plan. If a response exceeds that limit, it reports an incomplete result
and stops before showing or applying the proposal.
The same input revision
must still be current before transmission. Model choice is explicit and does not
create a canonical task-class approval.
A progress tab shows the selected model, guidance sources, total elapsed time, active
and finished file counts, and each file's current stage and elapsed time. The total timer
includes all files; a file timer includes both of its passes. Incomplete files are marked
as needing manual review. The CLI sends `[guidance-review]` JSON stage events with a
zero-based file index to standard error while keeping
the final JSON result on standard output. Updates describe preparation, each model pass,
coverage validation and warnings; they do not contain model reasoning or the source text.
Closing the progress tab leaves the review running. Completion opens the editable proposal.
The action then presents one editable combined diff per guidance file with `+` additions
and `-` deletions, plus the file plan, removal reasons, and warnings. Replace a
deleted line's leading `-` with a space to keep it; edit other text directly.
**Save guidance and import** saves the edited result, excluding deleted lines and
the first diff-prefix character. Subsequent imports retain your edits until the
guidance, linked evidence, inspected automation or generated CIS guidance changes. A local
`.cis/guidance-review.sha256` receipt avoids repeating an unchanged accepted review.
Non-entry-point files also offer **Retire this guidance file**. This explicit selection
moves the original into `.cis/retired-guidance/<content-hash>/<original-path>` outside
automatic discovery. Preserve unique obligations in retained files and remove incoming
links first. CIS rejects remaining reviewed links to retired files and newly introduced
Unicode corruption before applying the edited batch. Entry points cannot be retired.
**Cancel** or closing the review leaves the import unapplied.

To supply edited content through the CLI, add `--merge-edits <json-file>` alongside
`--merge-review <hash>`. The UTF-8 JSON file is an array with one entry for every
reviewed proposal: `repositoryPath` (the exact path from the proposal),
`relativePath` (an exact editable path from the proposal), and `content` (the complete
edited document), plus optional `retire: true` for an explicitly retired non-entry-point
file. Retirement preserves the original, not the editor's discarded text. Every proposed file must appear exactly once; arbitrary
paths and partial or duplicate edit sets are rejected.
Do not include diff prefixes or deleted lines in this CLI payload; the extension
removes those automatically when saving the UI review.
The review hash still identifies the original proposal and existing file, so a
stale review or mismatched edit set stops the batch before initialization.

Without a selected model, only exact duplicate instructions are removed. No small model
is selected automatically. Each model pass must account for every original instruction
exactly once and cite an existing CIS replacement, or an unchanged retained instruction
for duplicate removal; invalid or incomplete
passes are discarded in full. Each run allows ten minutes of model work, with up to
five minutes per pass and shorter timeouts as the run limit approaches. Preparation
and final validation add to the elapsed time. Validated pass responses are saved as
disposable local checkpoints under `.cis/local/guidance-review/`, including during a
model-assisted dry run; canonical files remain unchanged. Reuse requires the same
model, prompt, schema, guidance and full evidence revisions, and every reused pass
is validated again. Invalid or unfinished passes are not saved. If the limit is reached,
the UI offers **Continue review** or **Open incomplete proposal**. Continuing allows
another ten minutes and reuses validated passes; it does not apply the import.
CLI callers resume by repeating the same authorized dry run. `reviewPaused` identifies
a run that reached its time limit. This bounds each run, not the total time needed to
complete a large review. Checkpoint write failures appear as warnings.
Progress events include `elapsedMs`, and the extension retains streamed review events
in its output channel so late failures are not hidden by a truncated log.
Headings, fenced
examples and tables are included; fences and tables are reviewed as whole Markdown
blocks so unrelated commands and rows can remain. Front matter and the old managed
section are excluded from edits.
Editable guidance, generated guidance and reference evidence together remain subject
to the 90,000-character input limit. Suspected credentials, local context-budget limits,
unavailable models and failed passes produce explicit review warnings. No alternative
model is substituted. The review remains advisory even when both passes finish.
`fileMerges[].guidanceReview` records the provider, model, instruction coverage, completed
passes, reasons and warnings. `complete` means both coverage-checked passes finished;
it does not prove that every semantic judgment is correct. Final approval stays in the editor.

All sources in one invocation use the same documentation root. Run a separate import
for repositories that intentionally use another root; the registry preserves earlier
entries.

## Options

| Option | Required | Default | Effect |
| --- | --- | --- | --- |
| `--source <path>` | Yes | — | Selects existing repository paths; repeat or provide several values, up to 20. |
| `--root <path>` | Yes | — | Selects the repository-relative documentation root used to initialize every source. |
| `--workspace <path>` | No | Current directory | Selects the directory that owns `.cis/workspace.yml`. |
| `--participation <value>` | Yes | — | Selects `owned` product scope or external `dependency` context. |
| `--relationship <value>` | Yes | — | Uses `none` for owned repositories; dependencies require `producer`, `consumer`, or `bidirectional`. |
| `--component <id>` | No | None | Limits dependency context to a named component; repeat as needed. |
| `--ecosystem <id>` | Bootstrap only | — | Sets the ecosystem when self-import creates the authority. |
| `--product <id>` | Bootstrap only | — | Sets the product when self-import creates the authority. |
| `--ecosystem-name <name>` | No | Ecosystem ID | Sets its display name during bootstrap. |
| `--product-name <name>` | No | Product ID | Sets its display name during bootstrap. |
| `--dry-run` | No | `false` | Plans the complete batch and registry without writing. |
| `--merge-provider <provider>` | With model | None | Selects the guidance review provider explicitly. |
| `--merge-model <model>` | With provider | None | Uses this model for both whole-document review passes. |
| `--allow-remote-merge` | Remote review only | `false` | Authorizes sending the listed editable guidance, read-only reference evidence and proposed CIS guidance to that model. |
| `--yes` | No | `false` | Confirms the reviewed batch initialization and registry changes. |
| `--merge-review <hash>` | For a merge | None | Confirms the current files and generated guidance inputs returned by the dry run. A stale hash stops the batch before initialization. |
| `--merge-edits <json-file>` | For a merge | None | Supplies the final reviewed guidance for every proposal. Requires `--merge-review`. |
| `--format <format>` | No | `human` | Selects `human`, `json`, or `agent` output. |

## Canonical registry

```yaml
schema_version: 2
ecosystem:
  id: commerce
  name: Commerce
product:
  id: ordering
  name: Ordering
repositories:
  - id: ordering-docs
    path: .
    documentation_root: docs/cis
    role: authority
    participation: owned
    relationship: none
    components: []
  - id: orders-api
    path: ../orders-api
    documentation_root: docs/cis
    role: participant
    participation: owned
    relationship: none
    components: []
  - id: customer-profile
    path: ../customer-profile
    documentation_root: docs/cis
    role: participant
    participation: dependency
    relationship: producer
    components:
      - customer-api
```

Repository IDs must be unique. Every entry must agree with the repository's own
`.cis/repository.yml`. Missing repositories, duplicate paths or identities, invalid
configuration, and mismatched documentation roots invalidate the workspace.

## Effects and safety

- Uses the same classification, curated starters, and ownership rules as `cis repo
  init`, with a reviewable merge for existing `AGENTS.md` guidance. Other unmanaged
  files retain their collision checks.
- Bootstraps a missing workspace authority only when the workspace directory is itself
  one of the explicitly selected existing sources.
- Plans the complete batch before changing the first repository.
- Re-importing an existing identity explicitly reclassifies its boundary and remains
  idempotent; it never creates a duplicate entry.
- Never removes an earlier registry entry merely because it was omitted from a later
  import.
- Never builds graphs implicitly; use workspace graph build after import.
- Does not infer cross-repository graph edges.

If an initialization fails, run `cis repo doctor` for the affected source with the
same root, resolve its evidence-backed findings, and retry the complete import.

## Exit codes

| Code | Meaning |
| ---: | --- |
| `0` | Dry run, confirmed import, or unchanged reconciliation succeeded. |
| `2` | Workspace, source, root, configuration, or output format is invalid. |
| `3` | Review and confirmation are required before mutation. |
| `4` | A repository initialization or workspace identity collision exists. |

## Examples

```powershell
cis repo import --workspace C:\work\existing-api --source C:\work\existing-api --root docs/cis --participation owned --relationship none --ecosystem commerce --product ordering --dry-run --format agent
cis repo import --workspace C:\work\existing-api --source C:\work\existing-api --root docs/cis --participation owned --relationship none --ecosystem commerce --product ordering --yes
cis repo import --workspace C:\work\ordering-docs --source C:\work\orders-api C:\work\orders-web --root docs/cis --participation owned --relationship none --yes
cis repo import --workspace C:\work\ordering-docs --source C:\work\core-banking --root docs/cis --participation dependency --relationship producer --component accounts-api --yes
cis graph build --workspace C:\work\ordering-docs --format agent
```

## Related commands

- [`cis repo list`](cis_repo_list.md)
- [`cis workspace init`](cis_workspace_init.md)
- [`cis repo init`](cis_repo_init.md)
- [`cis repo doctor`](cis_repo_doctor.md)
- [`cis graph build`](cis_graph_build.md)

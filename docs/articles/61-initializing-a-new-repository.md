---
title: "Initializing a Newly Created Repository"
type: article
status: Active
series: "CIS in Practice"
series_order: 1
owner: "Andrew Spiteri"
last_reviewed: "2026-09-10"
review_cadence: on repository-initialization change
summary: "Establish governed intent, documentation, standards, and context before a new codebase accumulates undocumented decisions."
cis:
  stable_id: change-impact-studio:article:initialize-new-repository
---

# Initializing a newly created repository

A new repository has little source evidence and a large decision surface. That makes it
the best time to establish product identity, documentation authority, technical boundaries,
and delivery policy before implementation turns assumptions into precedent.

## Choose the authority shape first

Use `cis workspace init --ecosystem <id> --product <id>` when the new repository owns
its documentation and implementation locally. The same command also initializes the
repository and records it as the product authority. When one product will span several
repositories, use this repository as the combined authority or create a separate
documentation authority, then import owned participants. One workspace governs one
product; repositories owned by another product are dependencies, not additional
implementation targets.

## Begin with Git and a dry run

```powershell
git init
cis workspace init `
  --root docs\cis `
  --ecosystem new-product-ecosystem `
  --product new-product `
  --dry-run --format agent
```

The dry run creates no files. It shows the baseline documentation, standards, skills,
instructions, templates, references, and configuration CIS would add. It also reports
classification evidence, retained paths, warnings, and collisions. Reviewing this plan is
the first authority boundary: `--yes` confirms the displayed mutation; it does not bypass
an invalid path or collision.

## Apply the reviewed baseline

```powershell
cis workspace init `
  --root docs\cis `
  --ecosystem new-product-ecosystem `
  --product new-product `
  --yes
cis docs validate --strict
cis skills validate --strict
cis standards validate --strict
```

The starter documents remain Draft until their meaning is completed and reviewed. Empty
templates are not product authority merely because initialization created them.

Initialization also records two different kinds of state:

- canonical repository configuration, starter ownership, documentation, standards,
  skills, instructions, and governed references; and
- disposable scan, graph, index, cache, run, and feedback state beneath `.cis/local/`.

Deleting derived state must not remove product meaning or approval.

## Add source and reconcile

As projects appear, rerun initialization. Classification selects technology-specific
contracts, references, implementation skills, instructions, and standards. Source-derived
reference rows remain Draft observations. Human edits are preserved through starter
ownership and collision rules; obsolete unchanged generated artifacts move only through an
explicit, previewed quarantine operation.

## Build the first graph

```powershell
cis graph build
cis graph validate --strict
```

An unborn or early repository can use an exact graph build as a change baseline before
it has a meaningful commit history.

## Continue into product definition

Initialization creates the governed foundation; it does not invent the product. Start the
definition journey to establish business intent, technical direction, architecture,
contracts, experience direction, and the high-level delivery map:

```powershell
cis definition init
cis definition status
cis definition prepare --page business
```

For a greenfield product, unanswered business and technical choices remain human decisions.
The final definition activation becomes the exact product baseline used before starting the
repeatable feature loop.

## Choose identities that will survive implementation

The ecosystem and product IDs become durable workspace identity. Choose stable, plain
identifiers rather than a sprint name, repository folder, or temporary code name. Display
names can evolve; IDs connect authority, participants, graphs, product definition, and
future changes.

A standalone repository can be both documentation authority and implementation owner. If
the product later gains API, web, mobile, or infrastructure repositories, import them as
owned participants rather than creating another authority. A repository owned by another
product enters only as an explicit dependency.

## Review the first plan carefully

The dry run is the best point to check:

- resolved repository and documentation roots;
- ecosystem, product, authority, and repository identity;
- selected starter documents and governed references;
- standards, skills, and instructions chosen from current evidence;
- create, update, retain, and collision routes; and
- warnings about missing classification evidence.

A greenfield repository has little source evidence, so the baseline will be conservative.
That is expected. Do not add a framework marker merely to force a preferred standard;
record product and technical choices through the definition workflow.

## Turn starters into current authority

Initialization creates structure, not stakeholder meaning. Review the Draft product,
business, technical, architecture, contract, experience, and delivery artifacts. Replace
prompts with evidence-backed content, preserve open questions, and use the named human
authority for decisions and activation.

An empty template must not be marked Active to make validation quiet. Lifecycle should
describe the truth: Draft while meaning is being established, Active only after review.

## Reconcile as the repository grows

After adding the first project, package manifest, workflow, API route, or test suite, run
initialization again with the same root and identities. Review newly selected standards,
references, and implementation skills. Human-owned content remains intact; a collision
means the current file and generated expectation need reconciliation.

Then rebuild the graph so planning uses current source and document evidence. An unchanged
repeat should be idempotent.

## Start the first governed feature

Once the product-definition activation and technical intent are current, choose one small
observable outcome. Create a baseline-bound dossier, analyse likely impact, review every
finding, resolve material decisions, and build bounded work. This creates the first real
trace from intent to implementation and evidence before the codebase accumulates hidden
conventions.

## Avoid greenfield shortcuts

Do not infer a complete architecture from an empty folder, treat framework defaults as
product intent, commit local caches as authority, or create several workspaces for what is
really one product. Early convenience becomes expensive governance ambiguity later.

## Takeaway

Use a new repository's low implementation cost to make intent and authority explicit.
Initialize safely, complete the Draft knowledge, and reconcile as the codebase develops.

## Canonical CIS sources

- [`cis workspace init`](../manual/cis_workspace_init.md)
- [`cis repo init`](../manual/cis_repo_init.md)
- [CLI and repository initialization](../specs/cli-and-repository-initialisation-spec.md)
- [Classification-driven initialization](../specs/classification-driven-initialisation-spec.md)
- [High-level product-definition wizard](../specs/high-level-product-definition-wizard-spec.md)
- [`cis definition status`](../manual/cis_definition_status.md)

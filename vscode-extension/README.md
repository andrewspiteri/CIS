# Change Impact Studio for Visual Studio Code

This extension is a native delivery workspace over the `cis` CLI and canonical Markdown.
It contains no planning, graph, tracker, agent, testing, security, verification, approval,
or learning engine logic.

The Change Impact Studio Activity Bar container provides six focused Views:

- **Workspace** — repository authority, health, freshness, active change, review gate,
  and one next meaningful action;
- **Journey Map** — high-level product definition, technical definition, and the repeatable
  feature-delivery path through acceptance;
- **Changes** — current and closed dossiers with phase, lifecycle, gate, and stale state;
- **Evidence** — canonical Markdown, bounded context search, and explicit graph detail;
- **Runs** — distinct agent, workflow, test, security, diagnostic, and assurance outcomes;
- **Governance** — skills, instructions, standards, references, providers, and Doctor
  findings.

Purpose-built high-level definition wizard, Repository Doctor, technical-questionnaire, change, task, graph, design, agent-request, recommendation, run, and command
screens open in editor-area webviews. Canonical Markdown opens in VS Code's normal editor
or built-in preview and remains the editable authority.

Repository Doctor groups errors, warnings, and information, retains each finding's evidence
and suggested fix, and offers **Run command** beside **Copy command**. Run uses the configured
CIS executable for the report's authority and refreshes findings afterward. Commands containing
placeholders or shell syntax must be copied and completed separately. No fix is executed
automatically.

## Install

1. Install a compatible CIS CLI and run `cis --version`.
2. Run **Extensions: Install from VSIX…** in VS Code and select the packaged VSIX.
3. Open the repository or multi-root workspace to govern.
4. Set `cis.executablePath` only when `cis` is not available on `PATH`.

Optionally set `cis.actorIdentity` to the human identity CIS should record for audited
transitions and design or agent-run actions. Leaving it blank prompts at the point of use.

The setting is one executable path, not a shell command. Users needing a composite launch
must supply a wrapper executable.

## First use

Start with **Getting Started** at the top of the Workspace view or its book toolbar button.
You can also run **CIS: Getting Started** from the Command Palette. This page opens without
CLI queries and shows the next setup step for the selected folder: select an authority,
initialize it, import application repositories, and open the high-level wizard. Configured
authorities skip initialization. **How to use CIS** opens the bundled [beginner guide](GETTING_STARTED.md)
in Markdown preview; it is available offline and before workspace trust is granted.

**Initialize authority** collects explicit product/ecosystem identity and the documentation
root, shows the CLI dry-run plan, and asks you to confirm before creating files and building
the context graph. It does not open or approve the high-level definition automatically.

The Welcome View identifies no-folder, missing/incompatible CLI, a new empty project, an
existing unimported repository, initialization collision, untrusted, and ready states. Existing
source is routed through **Import existing repository**; an empty project is routed through
**Create CIS project**. Import performs a reviewed in-place self-import and context build without
copying or rewriting implementation files. In a multi-root workspace, select
the authority repository explicitly; the extension never silently switches it.
**Select Authority Repository** always opens a picker, including with one folder, and confirms
the selected path. For an initialized product, select its documentation authority folder.

Process execution requires workspace trust. Read-only canonical Markdown navigation may
remain available before trust is granted.

After repository creation or import, open **High-level product definition wizard** in the Workspace
View or run **CIS: Open High-Level Product Definition Wizard**. Its eight pages cover project
foundation, business definition, technical direction, solution architecture and diagrams,
contracts and dictionaries, experience direction with a one-page UI preview, delivery mapping,
and final review. Questionnaire edits save in the same retained panel. The final page can return
to any earlier page and offers one consolidated activation after all seven definition pages are
complete and current.
Loading shows progress and allows up to five minutes per definition command for large systems.
An incompatible CLI or failed status query is reported directly; it cannot produce an empty
foundation page. Doctor remains available as an optional foundation tool.

Only one wizard opens per authority. During preparation, refresh, or another wizard action,
the controls show a busy state and repeated clicks are ignored. Preparation and answer saves
reuse the CLI's returned page status. Supplementary question reads run sequentially. Repository
commands from this extension share a queue for the same authority, including background reads,
so they cannot compete over shared evidence files. Separate authorities can run independently.
Pending wizard and foreground actions take priority over queued background reads; an
already running command finishes first. Current CLIs bundle technical and UI questions
with definition status, reducing wizard load to two commands. Older CLIs retain the
sequential questionnaire fallback.

The existing **Next** actions remain available as focused shortcuts into the same canonical
records. CIS creates the workspace authority and context graph, creates
`specs/business-requirements.md` when requested, and guides those records through:

1. business-requirements validation and explicit approval;
2. one consolidated high-level technical questionnaire covering surfaces, stacks,
   architecture, data, identity, hosting, operations, assurance, AI, and constraints;
3. technical-intent generation with technology and architecture direction, editing, validation, and approval;
4. overall solution-design and component-sheet generation, editing, validation, and one atomic bundle approval;
5. one consolidated high-level UI questionnaire and a governed look-and-feel document covering shell, visual language, reusable components, responsive behavior, and accessibility;
6. high-level architecture diagrams, dictionary startup, and one representative UI-system preview;
7. high-level backlog derivation and review; and
8. one dependency-ready feature specification at a time after consolidated activation.

There is no separate specification form or extension-owned copy. The Markdown opened by
the guided action is the specification, and each refresh projects its current governed
state and one next meaningful action.

One repository generation shares identical in-flight and completed read-only CLI projections
across views. Repository file notifications, explicit refresh, and every mutation invalidate
that generation; structured not-ready status is cached as state rather than retried as a process
failure. Repository Doctor, graph status, and whole-repository index status also use disposable
content-aware caches beneath `.cis/local/`; full run events remain an on-demand evidence view
rather than background payload. An unavailable routing index is shown as **not built** and can
be generated explicitly from the Workspace View. The cancellable foreground build completes
all pending cards; startup never invokes the model or starts index generation.

Source, documentation, and graph changes in registered participant repositories invalidate
the UI cache even when only the authority folder is open. Generated build/dependency churn
is ignored. CLI graph validation also caches unchanged structure and database hashes under
`.cis/local/status/`; canonical state and freshness checks remain authoritative.

High-level architecture diagrams and the one-page visual-system preview remain available after
the wizard closes. They appear as rendered links in the Workspace product-definition stages and
Journey Map, and together under **Evidence → Definition visuals**. The complete Architecture,
Design, and Plans folders are also browsable from Evidence.

For an imported system, the wizard's **Business definition** page offers **Infer from
existing project** as its primary action. It lists and preselects product-owned repositories,
excludes dependencies and a docs-only authority, and includes an authority that also contains
application code. Confirm up to ten repositories and choose a provider. CIS uses the existing
protected BRD authoring flow with bounded graph evidence, refreshes stale selected graphs,
and keeps the result Review Required with source citations and unresolved questions. After
drafting, only the authority graph needs rebuilding. The wizard stays on Business definition
for draft/source review, independent review, and question resolution. Duplicate inference
or reference-drafting requests for the same authority are ignored while one is running.
The same action is available as **CIS: Infer Business Definition from Existing Project**.

For a generated Review Required BRD, **Draft business requirements from reference** lets
you select one or more non-sensitive plain-text or Word Open XML (`.docx`) files and assign
a Codex or Claude provider. CIS deterministically extracts bounded readable text from Word
documents, binds the original files by digest, runs the agent in an isolated scratch repository,
and applies only a one-file BRD result whose frontmatter and managed evidence blocks are
unchanged. When an agent created the draft, the next guided action is **Review BRD with
independent agent**. CIS selects a different review-capable provider, grants it read-only
access in an isolated workspace, and records structured advisory findings without allowing it
to edit or approve the BRD. Reusing the original authoring references is optional and requires
an explicit cross-provider disclosure confirmation. When the review completes, CIS refreshes
the Workspace again after filesystem notifications settle so its next action changes without
manual intervention. Open questions and human approval follow
the independent review only when it has no recommendations. Otherwise **Review recommendations**
opens one consolidated editor review screen with the full observation, location, and recommendation
for every finding. The human may approve items individually, edit exact remediation text, or
atomically approve every unchanged pending item as a batch. Earlier edited approvals are preserved.
No separate acceptance rationale or aggregate approval is requested. Completion mechanically locks
the exact bounded set and opens agent selection. Approved findings are applied by a provider different
from the reviewer, and a provider different from that reviser performs closure-only verification
rather than another broad refinement review. Agents never decide or approve findings;
legacy rejected recommendations remain explicit guardrails.

**Answer open BRD questions** opens one editor page with the complete question list,
deterministically selected BRD context, editable answer text areas, existing answer provenance,
and digest-bound advisory suggestions where the supplied evidence supports one. CIS tries local
AI first. Remote suggestion generation requires an explicit disclosure confirmation. Suggestions
never change the BRD by themselves: the named human must accept one or save edited text.
After the final answer, **Update BRD from answered questions** assigns a bounded one-file
revision that reflects every digest-bound human decision in the relevant business sections.
The updated BRD then requires a different-provider independent review; the journey cannot skip
from question completion directly to approval.

After BRD approval, **Define high-level technical direction** opens all 16 governed
`TI-Q-*` decisions in one editor page. Existing projects receive evidence-derived directions with
confidence and exact repository provenance; greenfield and ambiguous choices remain unanswered.
Each card includes context, common options, and an editable direction with one Save or Update
action. The current text is always the exact value recorded. Completed questionnaires remain
reopenable from the Workspace and Journey Map, and an updated direction automatically regenerates
the dependent technical intent. Saving an individual card stays on the questionnaire and preserves
its scroll position and focused card.
The final unanswered direction automatically generates the schema-4 technical intent, including the
choice baseline, logical component map, BRD-derived module tree and responsibility profiles,
stable integration-point catalog, primary interactions, architecture guidelines, and accepted
high-level decisions.

Once technical intent is Active, **Generate overall solution design** creates the canonical
implementation-independent architecture and component sheet. The first file explains context,
topology, ownership, integrations, trust, operations, recovery, assurance, and the later UI-design
handoff. The second retains the compact `TI-MOD-*` inventory, detailed responsibility profiles,
and `TI-INT-*` interaction catalogue. CIS validates and approves the exact pair as one bundle, so
there is no approval prompt per component or per file.

Once the solution-design bundle is Active, **Define high-level UI look and feel** opens all twelve
`UI-Q-*` choices in one editor page. Existing projects retain deterministic surface and
UI-framework evidence; product character, shell, density, theme, content, accessibility, feedback,
and constraints remain editable human choices. The last answer generates `design/ui-direction.md`.
After validation and one explicit approval, feature wireframes and rendered design packs consume
this shared direction instead of reinventing the shell and common components per feature.

## Governed agent work

An eligible planned task can request work through CIS-discovered Codex or Claude providers.
The confirmation shows the exact task, repository, provider, transport, isolation, run
mode, permission ceiling, and approval-request policy. The extension invokes `cis agent`
commands as argument arrays; it does not connect to providers, store chat history, or
handle credentials.

If provider authentication needs explicit setup, run **CIS: Authenticate Agent Provider**.
For Codex, CIS starts the provider-native ChatGPT browser OAuth or device-code flow and
never receives or stores the credential. Repository Doctor distinguishes a missing
executable from authentication that could not be verified; an unverified status is
advisory because an ambient Codex Desktop/App Server session may still work.

Foreground events, cancellation, recovery, resume attempts, results, and artifacts remain
bounded CIS evidence. Importing a result never approves scope, transitions a task, accepts
verification, or closes a change.

## Troubleshooting and reference

The full installation, first-use, navigation, settings, security, and troubleshooting
manual is `docs/manual/cis_vscode_extension.md` in the CIS repository. CLI authority,
exit codes, credentials, repository-local state, and canonical Markdown remain unchanged.

For imported systems, the business page first offers **Prepare existing-system context**. It
populates draft inventories and exposes coverage counts and dictionary links. **Infer from
existing project** also runs preparation automatically before sending cited dictionary details
to the selected provider. Reviewed content is preserved and unsupported business policy remains
an open question. Status reads reuse the last preparation report.

The contracts page offers **Preview entity diagrams** for the ERD. ERD entries in the CIS
tree also open Markdown preview by default. Expand an entity under **Relationship diagrams**
to view its relationships and multiplicities, or open the full-size SVG. CIS generates these
local images during dictionary preparation, so no Mermaid extension is needed. The relationship
table remains editable through **CIS: Open Canonical Markdown**; reviewed ERDs are preserved.

For an existing system, **Solution architecture and diagrams → Infer from existing repositories**
uses the same source/provider selection and isolated authoring flow as technical intent. CIS reads
implementation and dictionary evidence, drafts the overall design and component sheet together, and
renders four local SVG views directly in the wizard. Review observations, proposed changes and
unresolved deployment details together; citations stay in comments. The command palette also exposes
**CIS: Infer Solution Architecture from Existing Repositories**. Ordinary page preparation preserves
inferred narrative and refreshes the diagrams; approval and activation remain separate human actions.

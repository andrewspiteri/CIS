# Change Impact Studio for Visual Studio Code

This extension is a native delivery workspace over the `cis` CLI and canonical Markdown.
It contains no planning, graph, tracker, agent, testing, security, verification, approval,
or learning engine logic.

The Change Impact Studio Activity Bar container provides seven focused Views:

- **Product** — persistent product definition, registered repositories, feature navigation and workspace health;
- **High-level features** — every saved product feature, its definition progress and repository work;
- **Workflow guide** — high-level product definition, technical definition, and the repeatable
  feature-delivery path through acceptance;
- **Delivery changes** — current and closed dossiers grouped by explicit feature links, with other changes retained separately;
- **Documents** — canonical Markdown, bounded context search, and explicit graph detail;
- **Runs** — distinct agent, workflow, test, security, diagnostic, and assurance outcomes;
- **Governance** — skills, instructions, standards, references, providers, and Doctor
  findings.

Select a saved high-level feature to reopen its wizard directly. Different features can
stay open in separate tabs; page positions and unsaved text are retained per feature.
The **Delivery and acceptance** step includes an editable repository feature breakdown:
add multiple items per repository, describe their scope, select dependencies, and link
existing change dossiers. Independent items can be planned in parallel. Save the page
to retain the breakdown in the canonical feature request. Proposed work remains subject
to the existing product, backlog, feature-specification and implementation gates.

The feature list shares one CLI navigation projection and the workspace startup snapshot.
Opening the sidebar does not load every feature's plan or select an arbitrary active change.

Purpose-built high-level definition wizard, Repository Doctor, technical-questionnaire, change, task, graph, design, agent-request, recommendation, run, and command
screens open in editor-area webviews. Canonical Markdown opens in VS Code's normal editor
or built-in preview and remains the editable authority.

Repository Doctor groups errors, warnings, and information, retains each finding's evidence
and suggested fix, and offers **Run command** beside **Copy command**. Run uses the configured
CIS executable for the report's authority and refreshes findings afterward. Commands containing
placeholders or shell syntax must be copied and completed separately. No fix is executed
automatically.

## Add a feature from a prepared BRD

Technical direction, architecture, integrations, experience and delivery each present
several focused review questions with relevant source text prefilled. Use the numbered
question links to jump between topics. Tables, lists and headings render as readable text;
**Edit answer** opens the prefilled editor. **Save and continue** advances when all required
answers in the step are complete. **Save without leaving** keeps the current step open.
Existing reviews and unsaved drafts are preserved.

To update an existing feature's BRD, reopen it under **High-level features** and select
**Reimport BRD**. Choose the updated Markdown file, review the question changes and
use **Compare BRD changes** to open a diff in a separate tab. **Apply updated BRD**
retains the feature, repositories, saved answers and repository work, while requiring
renewed review. Reordered questions keep answers only when their text matches uniquely.
Previous source versions and saved reviews remain available through revision history.
Save or discard unsaved edits before reimporting; identical files leave review state unchanged.

**CIS: Open Feature Definition Wizard** opens the full resumable feature journey.
**CIS: Add Feature from BRD** remains an alias. The wizard uses the project wizard's
step navigation, current-page state, explicit outstanding work, and Back/Continue actions.

| Step | Review |
| --- | --- |
| Feature foundation | Source BRD, implementation repository and integration targets. |
| Business definition | Scope, outcomes, exclusions and the BRD's unresolved decisions. |
| Technical direction | Inherited product choices and feature-specific changes. |
| Solution architecture and diagrams | Existing architecture/C4 views and proposed component or interaction changes. |
| Integrations and dictionaries | API, event, data, permission and ownership changes. |
| Experience direction and UI impact | Existing visual baseline, affected journeys and generated proposed screens. |
| Delivery and acceptance | Dependencies, release boundary, migration, rollout and acceptance. |
| Review and next steps | Consolidated definition review and the existing governed delivery gates. |

Each page links directly to the applicable product baseline documents and the original
feature BRD. Relevant BRD excerpts prefill narrative fields; unsupported decisions remain
unanswered. **Save reviewed answers** records human identity in the canonical request,
updates the current page in place, and does not open an editor or refresh the workspace.
Navigation preserves drafts. Reopening restores the page, and saved requests are discoverable
from **Resume a feature**. Unsaved changes and baseline drift are visible and block final review.
Product documents and approvals are not rewritten by answering feature questions.

In **Solution architecture and diagrams**, use **Save answers and generate C4 diagrams**.
CIS combines the feature BRD and saved direction with the existing product architecture
to render context, container and component views directly in the step. Existing and
proposed elements are labelled. Open full-size diagrams in new tabs or save SVGs; edit
the architecture answers and regenerate to refine them. Generation uses the local model,
preserves the product architecture, and retains previous diagrams if generation fails.

In **Experience direction and UI impact**, use **Save answers and generate screens**
to create a gallery from the imported BRD and the experience answers shown in the form.
CIS discovers the existing UI baseline and uses a local model to propose application
screens with shared controls. Open a screen in a separate tab or save it as a JPG.
Review its proposed actions, failure states and source passage below the image.
These are draft layouts with illustrative data, not captures of the running application
or delivery design approval. Each screen has a feedback field and **Apply changes to this
screen**; the local model updates that image while preserving the others. **Not needed**
excludes and collapses a screen. **Include this screen again** reverses that choice.
Decisions and feedback are saved with the feature and survive reopening or regeneration.
Unsaved feedback is retained while navigating. Questionnaire drafts do not disable
screen feedback and are not saved by a screen review. Amendments use the displayed
preview and saved direction; save answers and regenerate to include questionnaire edits.
If the saved context makes a preview stale, CIS explains the block beside its review
buttons and offers regeneration. If generation fails, the previous image
and saved request remain available to retry. Edit the experience answers and generate
again when the overall direction changes.
Cached screens reopen without model calls; BRD, saved direction or UI baseline changes
mark them stale. A failed generation keeps the previous gallery available. Only opening
the experience step checks the screen cache, so startup gains no additional queries.

Use **CIS: Add Feature from BRD**, **Add feature from BRD** on Getting Started,
or the **+** toolbar action in Workflow guide and Changes. The form selects the local
Markdown BRD, feature name, new or existing implementation repository, documentation
folder and integration repositories. **Review setup** shows the proposed setup and
unresolved source decisions. **Create feature request** creates the local repository
when requested, registers it as product-owned, and retains the unchanged BRD under
the authority. No model is called during intake.

The resulting request is Draft and works with a backlog that has no planned work.
Use **Review feature request** and **Open original BRD** to review it beside the page.
Scope review and reconciliation into the governed backlog precede feature specification
and implementation planning. This action does not approve the BRD or choose a stack.

**Delivery and acceptance** presents user stories in three editable lists: **Foundation**
(required regardless of release scope), **MVP** (first release), and **Post-MVP** (later
delivery). Expand each story to review its acceptance outline. CIS prepares the lists
from the retained feature BRD without a model call; exclusions stay outside the lists
and uncommitted future ideas are labelled for review. Each MVP story has **Move to
Post-MVP**; each Post-MVP story has **Promote to MVP**. These buttons move the complete
story into the other draft list without refreshing the workspace. Use **Edit user stories**
to refine the wording, then save alongside the release questions and repository breakdown.

Before treating these requirements as implementation work, use **Reconcile with existing
implementation**. CIS compares them with code in all owned repositories, including those
outside the original feature selection. Review proposed reuse, extensions, new work and
scope conflicts, with owning repositories and evidence. The ownership question records
where responsibilities must remain. Reconciliation saves only that answer, uses a local
model, and caches its proposals. **Use reconciled stories as draft** keeps review and
saving explicit; it never approves implementation or silently replaces saved stories.

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

Start with **Getting Started** at the top of the Product view or its book toolbar button.
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

After repository creation or import, open **High-level product definition wizard** in the Product
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
the controls show a busy state and repeated clicks are ignored. Each newly loaded wizard
page synchronizes that state with the extension, so a completion message missed while
replacing the page cannot leave it locked. Revealing a retained wizard also resynchronizes
the controls. Error and review-follow-up notifications do not keep a completed action busy
while waiting for dismissal. Preparation and answer saves
reuse the CLI's returned page status. Supplementary question reads run sequentially. Repository
commands from this extension share a queue for the same authority, including background reads,
so they cannot compete over shared evidence files. Separate authorities can run independently.
Pending wizard and foreground actions take priority over queued background reads; an
already running command finishes first. Current CLIs bundle technical and UI questions
with definition status, reducing wizard load to two commands. Older CLIs retain the
sequential questionnaire fallback.

Entering **Experience direction and UI preview** automatically discovers the current UI
baseline from owned repositories through `cis ui-direction baseline`. It shows each
interface's frameworks, shared shell and components, styling, font declarations, color
swatches, responsive patterns and feedback markers, with file links opening beside the
wizard. Source limits remain visible. The scan uses bounded local inputs and a content-hashed
cache; ordinary startup and other wizard pages do not run it. Refreshing the Experience
page checks the current implementation again.

The page shows a **JPG control sheet** for each discovered interface before the source details.
It renders labelled examples of detected control families using observed theme values. Use
**Save JPG control sheet** to export and open the image beside the wizard. Sheets are source-based
reference renderings: example content, control geometry and states need comparison with the running
interface before exact appearance is approved. Rendering uses the webview, without installing or
running frontend packages, and is available before the UI questionnaire is complete.

Experience readiness reports preview availability separately from UI-direction readiness.
The discovered sheets occupy the one-page visual preview section even when a governed UI
document has not yet been generated. The page identifies upstream review, missing questions,
unanswered questions, stale answers, or direction preparation as the next step. It never
marks direction complete merely because a source-based preview exists.

The baseline can be reviewed while technical or architecture decisions are still open;
links identify those prerequisites. Once the architecture is ready, the wizard prepares
its questions automatically. Observed implementation details prefill review suggestions,
preserving existing human answers. Saving directions and approving the baseline remain
explicit human actions.

The architecture page provides **Approve architecture and diagrams**, with direct review links
to the overall design, component sheet and diagram set. **Approve business requirements** belongs
to Business definition, and **Approve technical intent** belongs to Technical direction. Missing
prerequisites link back to those steps; architecture approval never accepts those documents implicitly.
Stale-source and diagram warnings remain visible beside the controls. Approval covers the three
architecture artifacts together, preserves diagram currency, and keeps the final whole-product
activation on the Review and activate page.

When inferred architecture predates the current technical direction, the wizard shows
**Reconcile architecture with technical direction** as the next action. This runs the existing
repository/provider selection and inference workflow, then regenerates the diagrams. Prepare
preserves the inferred narrative and explains when reconciliation is required; Refresh only
rechecks status. A cancelled or failed reconciliation leaves approval blocked. Preparing diagrams
keeps the architecture page open for review.

The Delivery map offers **Create candidate backlog** and **Record no planned work**. Existing
capabilities are not automatically treated as unfinished delivery scope. The no-work choice
records an empty scope with named human provenance and still requires final baseline approval.
Both actions run in place, show their outcome and preserve the current wizard step. Existing
backlog items cannot be discarded by the no-work action.

Technical direction separates confirmations of recorded questionnaire answers from additional
product decisions. Every unresolved decision automatically prefills one editable **Answer**
field with suggested text, using current questionnaire answers and topic-specific proposals.
Review or edit that text and choose **Save answer**; no separate reason field is required.
Suggestions are generated locally by CIS whenever status is read, including for newly
discovered decisions, and remain advisory until saved. Existing saved answers and unsaved
edits take precedence. Previous reason text is preserved within the answer. Saving refreshes
readiness; changed evidence must be reviewed before retained edits can be saved.
Saving stays in the wizard and reads one fresh definition projection, without opening a command
tab or refreshing the workspace views. A confirmation identifies the resolved decision and
the remaining review count; the saved decision stays visible under Recorded decisions. If the
write succeeds but the read fails, the wizard reports that the answer was saved and asks for
Refresh without declaring the write a failure.

The product wizard provides focused next-step actions into the same canonical
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
be generated explicitly from the Product view. The cancellable foreground build completes
all pending cards; startup never invokes the model or starts index generation.

BRD question-list and review-freshness queries share this cache too; reading either must not
invalidate other panels. Explicit uncached reads leave unrelated cached results intact. Mutations,
watched input changes and manual refresh still invalidate the generation. Each executed JSON command
logs `[timing] run=...ms; queue=...ms; exit=...` in the CIS output channel, distinguishing process time
from time waiting behind another command for the same authority.

Startup uses `cis workspace snapshot` to load the common projections in one process,
sharing graph input checks and classification across them. Per-query results, exit codes,
and `[snapshot]` timings remain visible. Review-run details and other queries stay on demand.
A watched input change during loading discards the obsolete snapshot and retries once;
concurrent readers share the replacement. Repeated changes remain a visible freshness
error. Queries outside the fixed startup set execute directly. Older CLIs without the
optional snapshot command use individual queries.

Source, documentation, and graph changes in registered participant repositories invalidate
the UI cache even when only the authority folder is open. Generated build/dependency churn
is ignored. CLI graph validation also caches unchanged structure and database hashes under
`.cis/local/status/`; canonical state and freshness checks remain authoritative.

High-level architecture diagrams and the one-page visual-system preview remain available after
the wizard closes. They appear as rendered links in the Product product-definition stages and
Workflow guide, and together under **Documents → Definition visuals**. The complete Architecture,
Design, and Plans folders are also browsable from Evidence.

The wizard's **Technical direction** page distinguishes completed questionnaire answers
from the technical-intent document's open decisions. It lists each remaining decision,
its required gate, status and rationale, with a link to the exact Markdown row in a
pinned editor tab beside the wizard. Completed answers are collapsed for optional review.
The next action addresses document decisions, unanswered questions, stale evidence or
incomplete content as appropriate; preparation and re-inference are labelled Optional
when the existing draft is current. Refresh rechecks saved edits without selecting or
approving any technical decision.

The wizard's **Business definition** page shows a readiness summary above the tools. **Needed**, **Complete**,
and **Optional** labels explain which actions address the current findings. The suggested
next step comes from CIS, including changed repository evidence and outstanding source
decisions. Source decisions show document names with a source link, direct Adopted,
Reference, and Rejected choices, and a reason field. **Save decisions** saves all edited
entries together through CIS and refreshes readiness once. Unfinished entries survive
navigation and refresh; changed evidence requires explicit review before saving retained
edits. Existing drafts do not need to be regenerated to review their sources.

Document titles are links that open pinned editor tabs beside the wizard, allowing several
sources to remain open for review. Each card shows a cached local-model summary or a
document excerpt. **Summarize documents locally** generates missing summaries with the
configured local model; source changes invalidate them. Opening or refreshing the wizard
does not call the model, and summaries never choose an assessment for you.

For an imported system, the wizard's **Business definition** page offers **Infer from
existing project** for creating a draft. It lists and preselects product-owned repositories,
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
an explicit cross-provider disclosure confirmation. When the review completes, CIS loads
the retained run directly, initializes pending recommendations, and opens the report before
refreshing workspace views. Follow-up failures identify the review as successful and allow
continuation from the retained run without another provider execution. CIS refreshes the
Workspace again after filesystem notifications settle so its next action changes without
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
reopenable from the Product and Workflow guide, and an updated direction automatically regenerates
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
renders C4 context, container and scoped component SVG views in the design and wizard. Review observations, proposed changes and
unresolved deployment details together; citations stay in comments. The command palette also exposes
**CIS: Infer Solution Architecture from Existing Repositories**. Ordinary page preparation preserves
inferred narrative and refreshes the diagrams; approval and activation remain separate human actions.

---
title: "cis brd feature wizard delivery"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-21"
review_cadence: on command change
cis:
  stable_id: change-impact-studio:manual:cis-brd-feature-wizard-delivery
---

# `cis brd feature wizard delivery`

Compares feature requirements with existing code before treating a story as new implementation.

```text
cis brd feature wizard delivery status --slug <feature> --workspace <authority> --format json
cis brd feature wizard delivery prepare --slug <feature> --workspace <authority>
  --expected-revision <wizard revision> --format json
```

In **Delivery and acceptance**, review the ownership question, then select
**Reconcile with existing implementation**. The extension saves only this ownership
answer before reconciliation. Other answers, story drafts and repository work remain
in the form. Prepare uses a local model with no remote fallback. Status checks the
derived result without generating text or writing canonical documents.

Opening this step also reports initial code matches. If a recorded ownership direction
retains maintenance in an existing application, a matching maintenance requirement and
existing maintenance entry points produce an ownership-overlap warning without a model.
The model cannot turn that overlap into a new maintenance implementation recommendation.

CIS searches story titles and acceptance requirements against code contents and paths in all registered product-owned participant
repositories, including repositories absent from the feature's original integration
selection. Dependency repositories, documentation, generated folders, test fixtures,
scripts and sensitive-looking content do not establish owned implementation. File
names and content matches route to source; they do not establish that a requirement is satisfied.
CIS matches capability names and related operation or data concepts within identifiers.
It preserves requirement clause boundaries and excludes comments and string literals from
search signals. Shared words scattered across a file do not qualify it as evidence.
Root-level test scripts are excluded along with test directories. Selected excerpts show
the relevant operation in source order, with a link to the complete file.

Each code lead explains why it was selected. A **Capability candidate** names the capability
or a relevant maintenance operation. A **Possible integration point** identifies related
code where new behaviour might connect. For example, product selection may provide an
entry point for referral tracking; it does not establish click storage or retention.
**Requirement checks** links each acceptance requirement to related code, or reports that
no supporting code was established. Neither label claims implementation coverage or absence.
Raw excerpts are available under **View code excerpt**, beneath the relevance explanation.

Each assessment proposes **Reuse existing**, **Extend existing**, **New implementation**,
**Scope conflict** or an inconclusive assessment, with existing capability, remaining work,
owning repositories and supporting code excerpts. Foundation, MVP and Post-MVP remain
release categories, independent of these implementation assessments. Missing or weak
evidence remains unresolved. Existing-code claims require supplied code references and
valid product-owned repository identities. A complete-reuse proposal is rejected if it
has only integration points or lacks relevant code for any acceptance requirement.
With only integration points, a reuse or extension proposal must name an observed code
operation. Claims that the whole capability is implemented are rejected.
All model conclusions still require review.

The wizard distinguishes **Not yet assessed**, **No matching code found**, and
**Assessment inconclusive**. Each story explains whether the model was not run, no
matching code was found within search limits, confidence was low, an owner was missing,
or an existing-code claim lacked evidence. These states describe limits of the assessment;
they are not findings that the product is missing a capability.

To resolve a story, open **Resolve the delivery decision**, review its full requirements,
open the supporting code links, and choose **New work**, **Extend existing**, **Reuse existing**
or **Exclude from this feature**. Planned work and code references are prefilled as
suggestions. A valid model proposal also suggests treatment and owners. For an inconclusive
assessment, choose these explicitly: a code match does not automatically select extension
or assign ownership, and no match does not automatically select new work.
Edit the suggestions, then select **Save story decision**. New work does not require an
existing-code reference. Reuse and extension require an existing file in an owned repository.
Keep relevant paths or add one per line using `repository-id/path/to/file`. The planned work
records the decision without a separate reason field. **Discard story draft** clears an
unsaved card edit. Save or discard edited cards before saving the delivery page.

Saving a decision changes that story to **Decision saved**, retains actor and evidence hashes
in the canonical feature request, and preserves all other answers, story lists and repository
work. It does not call a model or mark implementation complete. Changed BRD, direction,
repository registration or implementation makes the retained decision require a recheck.
Model assessment and human planning choice remain separate. A saved exclusion or ownership
choice does not silently rewrite conflicting BRD requirements.

The CLI uses the existing `cis brd feature wizard save --input <json> --workspace <authority>`:

```json
{
  "slug": "feature-slug",
  "page": "delivery",
  "answers": {},
  "actor": "Reviewer",
  "expectedRevision": "<wizard revision>",
  "deliveryReview": {
    "storyId": "<delivery story id>",
    "treatment": "new",
    "owners": ["feature-repository"],
    "plan": "Implement the reviewed acceptance requirements.",
    "evidencePaths": [],
    "expectedInputHash": "<delivery input hash>"
  }
}
```

Send a story decision separately from page answers and repository work. Reopening the
wizard retains the saved choice. Use the reconciled stories as a draft when ready to
include those decisions in the three release lists, then review and save those lists.

The saved ownership direction is included explicitly. A conflict with older BRD wording
must be exposed rather than silently removing that requirement. The source text and its
hidden requirement references remain in the suggested lists for reconciliation.

**Use reconciled stories as draft** copies the proposed lists into the form. It does not
save them, resolve conflicts, approve scope or create implementation tasks. Existing
draft edits require confirmation before replacement. Save the page after reviewing the
scope and acceptance requirements. Saved story lists are never replaced on status reads.

The disposable cache under `.cis/local/feature-delivery/<slug>/` is bound to the source,
saved direction, repository registration, source-file inventory and selected file hashes.
Code additions, edits or changed direction invalidate it. Preparation uses an exclusive
lock and checks inputs again before writing the cache. Concurrent changes preserve the
previous result and require a retry. Normal wizard startup does not scan implementation;
the separate check runs when the delivery step is opened.

Code search caches the selected file routes against the BRD and source inventory.
Repeated checks verify file hashes and reconstruct excerpts from source instead of
repeating the full search. Cached text is not treated as implementation evidence.

Selection is bounded to 80 stories, 40 MiB of code and files no larger than 256 KiB,
with up to eight excerpts for each story. Omitted content is reported. These excerpts cannot prove complete implementation or absence
of a capability. Review code and full acceptance criteria before planning work.

Exit codes: `0` for successful status/preparation; `5` for invalid, unavailable or changed
inputs, concurrent preparation, unavailable local generation or rejected output.

See [wizard status](cis_brd_feature_wizard_status.md) and [save](cis_brd_feature_wizard_save.md).

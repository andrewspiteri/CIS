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

CIS reads bounded code excerpts from all registered product-owned participant
repositories, including repositories absent from the feature's original integration
selection. Dependency repositories, documentation, generated folders, test fixtures,
scripts and sensitive-looking content do not establish owned implementation. File
names route to source; they do not establish that a requirement is satisfied.

Each assessment proposes **Reuse existing**, **Extend existing**, **New implementation**,
**Scope conflict** or **Needs investigation**, with existing capability, remaining work,
owning repositories and supporting code excerpts. Foundation, MVP and Post-MVP remain
release categories, independent of these implementation assessments. Missing or weak
evidence remains unresolved. Existing-code claims require supplied code references and
valid product-owned repository identities. All model conclusions still require review.

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

Selection is bounded to 80 stories and 120 source files, with up to eight excerpts for
each story. These excerpts cannot prove complete implementation or absence
of a capability. Review code and full acceptance criteria before planning work.

Exit codes: `0` for successful status/preparation; `5` for invalid, unavailable or changed
inputs, concurrent preparation, unavailable local generation or rejected output.

See [wizard status](cis_brd_feature_wizard_status.md) and [save](cis_brd_feature_wizard_save.md).

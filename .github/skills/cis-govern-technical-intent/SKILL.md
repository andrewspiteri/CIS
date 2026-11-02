---
name: cis-govern-technical-intent
description: Load supplied Technical Intent through the CIS CLI, reuse interpreted source facts, record explicitly authorised answers with applicable revision checks, and initialize, review or approve governed technical direction. Use for supplied technical inputs and after BRD approval before change planning.
---

# Govern Technical Intent

## Supplied direction and authorised answers

When the user supplies an existing Technical Intent, BRD, or pair, first follow [Supplied BRD and Technical Intent](../cis-govern-business-requirements/SKILL.md#supplied-brd-and-technical-intent). Use `definition documents` and `definition prepare --page technical` before authoring a replacement or starting a full questionnaire. The graphical wizard is optional. In setup, inspect `definition status --format json` so selected working facts remain visible with their pending-adoption provenance.

After inspection identifies a genuine unresolved item, record only the decision explicitly authorised by the named human. The following examples use replacement placeholders; `<workspace>` identifies the authority workspace.

For a `TI-Q-*` factual choice, read the current `technicalQuestions.questions` context and source evidence in definition status, then use:

```sh
cis definition answer --workspace "<workspace>" --page technical --id "<TI-Q-ID>" --answer "<authorised answer>" --actor "<human decision-maker>" --format json
cis definition status --workspace "<workspace>" --format json
```

This questionnaire operation has no revision-token option. Use the latest status and review any source conflict it returns; the answer does not silently amend an authoritative document.

For a `TI-DEC-*` document decision, read the current `pages` entry whose `id` is `technical`, then `guidance.technicalDecisions`; alternatively inspect `cis technical-intent status --workspace "<workspace>" --format json` at `validation.decisions`. Review the selected item's context, affected scope and `reviewToken`. Write a small UTF-8 input file outside CIS-managed state with the authorised resolution and that exact token:

```json
{
  "resolution": "<authorised resolution>",
  "reviewToken": "<current reviewToken>",
  "reason": "<human-provided rationale, when supplied>"
}
```

`reason` is optional; omit it when no separate rationale was supplied. Submit the file through the existing revision-bound operation:

```sh
cis technical-intent decisions resolve "<TI-DEC-ID>" --input "<decision-answer.json>" --actor "<human decision-maker>" --workspace "<workspace>" --format json
cis definition prepare --workspace "<workspace>" --page technical --format json
cis definition status --workspace "<workspace>" --format json
```

A stale token returns a conflict without saving the decision. Re-read the current item and its relevant changes before requesting or recording renewed authority. Saving a resolution preserves its actor and provenance and does not approve the Technical Intent. Reopening uses the same workspace and persistent state; do not edit managed JSON, frontmatter or session records to progress.

Inspect structured readiness and errors as well as the command exit code. Imported working direction, accepted facts, unresolved choices, source conflicts and required baseline adoption remain distinct. Direct low-level status after adoption remains useful for domain diagnostics; selected working facts outside the coordinator retain their unadopted status.

Source adoption preserves the supplied facts without settling missing choices. Necessary unresolved `TI-Q-*` choices block dependent technical work even after adoption; absent optional capabilities remain nonblocking. Inspect the identified remaining choices and record only authorised answers through the procedure above.

Treat the workspace as authority for exactly one product. Derive product technology and
module direction only from owned repositories. Record producer, consumer, and
bidirectional dependency repositories as bounded integration evidence; never infer their
technology as product direction or plan writes against them.

## Workflow

Use the supplied-input procedure above when documents are provided. The discovery option below fills an implementation-evidence gap; it does not replace supplied authoritative direction.

Apply the ordinary generation and approval prerequisites below after interpreting supplied inputs. Inspecting selected working direction through the coordinator does not require prior baseline approval.

For an imported existing product, the maintainer may request review-only discovery while
the BRD is still under review. Use `cis agent discover technical-intent --reference <owned-repo>
--actor <human> --repo <authority>` for local preparation, then `cis agent author technical-intent`
with an explicitly selected provider for an implementation-based narrative. This draft exception
does not approve the BRD, answer unresolved questions, resolve technical decisions or enable
downstream implementation. Preserve code-supported current behavior separately from proposed
direction. Review-only discovery precedes, and does not replace, the approval workflow below.

1. Confirm `cis brd status --workspace <workspace>` reports Active, valid, and current.
2. With supplied documents, first use the input path above and reuse their interpreted facts. Otherwise run `cis technical-intent questions init`. Retain source-derived and implementation-derived answers with provenance; present only necessary unresolved choices or explicit overrides for human input, including in greenfield projects. Record each authorised human answer through the existing questionnaire operation; advisory directions remain suggestions.
3. Build and strictly validate the workspace graph.
4. Run `cis technical-intent init --workspace <workspace> --format agent` to interpret a selected supplied intent or generate a genuinely new scaffold from the completed questionnaire, Active BRD, classifications, standards, and graph builds. A supplied intent does not require the user to transcribe its settled facts into the questionnaire before interpretation; required adoption remains governed.
5. Review the generated choices, component map, BRD-derived `TI-MOD-*` module tree and responsibility profiles, `TI-INT-*` integration-point catalog, business baseline, technical surface, standard provenance, and architecture guidelines without broadening the BRD or silently contradicting a human answer.
6. Confirm that each business capability has one owning module; each module states purpose, BRD coverage, owned and excluded responsibilities, inputs, outputs, data/state, security, failure/recovery, and verification. Merge or split candidates only with preserved requirement traceability and explicit ownership migration.
7. Confirm every cross-module, external-system, identity, persistence, model, event/file, operator, and runtime handoff records owner, direction, contract/data, authentication and authorization, compatibility, consistency, idempotency, timeout/retry, failure/recovery, observability, and test expectations. Exact endpoints and schemas belong in linked canonical reference dictionaries.
8. Complete architecture boundaries, data and consistency, contracts, security/privacy, operations/recovery, quality evidence, and delivery constraints. Preserve substantive human-authored sections.
9. Record later bounded unresolved choices in `Open technical decisions` with stable `TI-DEC-*` identity, gate, status, and rationale. Agents may propose options but never select or defer them without explicit human authority.
10. Run `cis technical-intent validate` and `status`. Resolve every placeholder, unresolved decision, stale questionnaire/BRD/standard digest, and participant-baseline drift.
11. Run `cis technical-intent approve --reviewer <human> --reason <rationale>` only after explicit authority.
12. After approved implementation changes participant graph IDs, run `cis technical-intent refresh`; request renewed approval only for material semantic change.
13. Rebuild and strictly validate the workspace graph after approval.
14. Run `cis solution-design init`; review and approve the overall architecture and component sheet as one bundle. Then complete and approve `cis ui-direction` before backlog, feature-design, change, or plan commands.

## Guardrails

Preserve managed baseline and derived-evidence markers and identities. Do not turn ambiguous evidence or absence of code into a derived fact, record advisory questionnaire text without human action, treat Draft, Ready for Approval, or Stale as Active, approve on a user's behalf, edit approval hashes, or bypass the readiness gate.

## Human-readable content

Apply the [shared content standard](../../../docs/standards/human-readable-content-standard.md) to in-scope prose. Use [cis-technical-writing](../cis-technical-writing/SKILL.md) for documents and reports, [cis-ux-writing](../cis-ux-writing/SKILL.md) for interface wording, and [cis-content-review](../cis-content-review/SKILL.md) for source-aware review followed by a separate reader check. Preserve this workflow's authority, evidence, privacy and lifecycle rules. Clarity does not remove legitimate uncertainty or mandatory specification sections.

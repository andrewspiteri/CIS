---
name: cis-content-review
description: Review human-facing wording against authoritative sources for intent preservation, then check whether an unfamiliar reader can understand the state, task, and consequences. Findings are advisory, not approval.
---

# CIS content review

## Purpose

Catch meaning loss and reader confusion before a wording change is presented as complete.

## When to Use

Use for rewritten requirements, generated explanations, major documentation changes, and interface messages that explain blockers, permissions, or approvals. Scale review effort to the risk. Routine spelling edits do not require another model run.

## Inputs

Read the original source, revised output, relevant source/state revision, and [shared standard](../../../docs/standards/human-readable-content-standard.md). Use the [review fixtures](../../../docs/references/human-readable-content-fixtures.json) as examples, not as a substitute for current evidence.

## Workflow

1. Establish the comparison scope and what evidence is available. Distinguish full-source review from excerpt-only review.
2. Compare actors, obligations, negation, conditions, exclusions, quantities, sequence, acceptance intent, uncertainty, and authority. Flag omissions and unsupported additions.
3. Check exact identifiers, commands, protected blocks, source associations, and machine contracts mechanically where possible. Do not treat those checks as proof of semantic equivalence.
4. Separately assess the reader's experience. For an unfamiliar-reader test, provide only the rendered text or screen and realistic task questions, without authoring notes. Ask what is happening, what action is available, what it changes, and what remains unapproved or unknown.
5. Compare reader answers with the source-backed expected answers. Identify the passage that caused confusion. A source-aware reviewer is appropriate for correctness; a context-limited reader is appropriate for comprehension. Do not conflate the two.
6. Report specific changes needed and stop within the requested scope. Route policy changes or unresolved ambiguities to the appropriate human. Do not self-approve a revision.

## Output Expectations

For each material finding, record location, issue, relevant source, proposed correction, and review limit. State whether the check was mechanical, model-assisted, human, or not performed. Do not claim real-user usability testing when only model review was performed.

## Guardrails

Approval remains with the authorized human and existing CIS workflow. Do not force a readiness or success label because prose sounds clear. Never automatically rewrite protected or approved material. Retain privacy restrictions and disclose no repository content to another provider without the required authorization.

## Related Files

Use [cis-technical-writing](../cis-technical-writing/SKILL.md) or [cis-ux-writing](../cis-ux-writing/SKILL.md) to prepare corrections. Existing domain review/approval workflows remain authoritative.

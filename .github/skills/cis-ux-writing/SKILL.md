---
name: cis-ux-writing
description: Write or review CIS interface labels, helper text, statuses, errors, confirmations, empty states, and next-step guidance using checked application state and accurate action consequences.
---

# CIS interface writing

## Purpose

Make the user's task and next action understandable without changing application behavior.

## When to Use

Use when creating or changing interface wording, including generated text shown inside CIS. Review connected messages as a journey rather than treating each string in isolation.

## Inputs

Read the [shared standard](../../../docs/standards/human-readable-content-standard.md), the actual renderer, its checked state, action handler, permissions, and source document where relevant. Confirm the reader's role. Do not infer a command's effects from its label alone.

## Workflow

1. Identify what the user is trying to do, what is currently true, and what prevents progress.
2. Write a direct main message and the minimum explanation needed for the decision. Put relevant warnings before the action.
3. Use an action label that describes the actual effect. Distinguish saving an answer, reviewing evidence, approving scope, refreshing a derived view, and executing work.
4. Give the next supported step. If no recovery action or cause is known, say so and expose safe diagnostics; do not invent a button or a cause.
5. Keep exact technical identifiers in details or alongside the plain label when necessary. Leave material blockers and permission consequences visible.
6. Check empty, loading, failed, partially complete, stale, permission-denied, and completed variants. Confirm that shorter wording does not falsely imply approval or successful persistence.
7. Check keyboard-readable explanations, accessible labels, zoom, wrapping, and a narrow editor pane. Apply cis-content-review to changes that explain a decision or approval boundary.

## Output Expectations

Return the proposed text with its state/action association and any functional gap that prevents an accurate message. For a copy-only change, preserve action IDs, reason codes, data contracts, and permission logic.

## Guardrails

Do not replace the domain state machine with presentation logic. Do not require a model for standard statuses. Do not rename all occurrences of an internal concept. A terminology change needs a contextual mapping, not a global search-and-replace. Existing sanitization, privacy boundaries, and confirmation requirements remain unchanged.

## Related Files

Read the [terminology reference](../../../docs/references/human-readable-content-terms.md) and [content-review skill](../cis-content-review/SKILL.md). Start with the target repository's actual interface renderers and action handlers; inspect their checked state and permission boundaries.

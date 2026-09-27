---
name: cis-technical-writing
description: Write or revise CIS documents, human-facing reports, explanations, and change descriptions in clear language while preserving requirements, evidence limitations, identifiers, and approval boundaries.
---

# CIS technical writing

## Purpose

Help the intended reader understand the content without changing its meaning or authority.

## When to Use

Use when drafting or revising documentation, generated narrative, human-readable command explanations, review findings, or change descriptions. Use cis-ux-writing for interface labels and messages. This skill complements, rather than replaces, cis-documentation and cis-add-documentation.

## Inputs

Read the relevant authoritative source, the intended audience and task, the existing document template, and the [shared standard](../../../docs/standards/human-readable-content-standard.md). Identify material unknowns and any protected blocks before editing.

## Workflow

1. Identify the reader's goal and the document's main purpose. Use learning steps, task instructions, lookup facts, or explanatory narrative as appropriate. Preserve required governance sections and metadata.
2. Record the facts that cannot change: actors, requirements, exceptions, limits, ordering, uncertainty, source identities, and approval scope.
3. State the useful conclusion first. Explain the relevant behavior through concrete actors and outcomes. Define necessary unfamiliar terms and retain exact technical names where a reader needs them.
4. Remove repetition and empty wording. Keep complete explanatory sentences. Do not force short sentences, eliminate legitimate qualifications, or reword unchanged content merely for variety.
5. Put supporting detail where it helps the reader, without hiding a condition that changes what they may do. Preserve the established evidence-presentation contract.
6. Compare the rewrite with the original facts. Apply cis-content-review to the in-scope output. Report unresolved ambiguity and limits on verification.

## Output Expectations

Produce the requested text and a brief change note where useful. For substantive rewrites, identify any unresolved questions or facts that could not be verified. Do not append a large self-review report to every user-facing document.

## Guardrails

Do not invent examples that look like actual product behavior. Mark hypothetical examples. Never rewrite protected managed blocks, rename machine identifiers, approve a document, or alter scope as a writing improvement. A source overview is selective unless completeness has actually been established.

## Related Files

Read the [shared standard](../../../docs/standards/human-readable-content-standard.md), [terminology reference](../../../docs/references/human-readable-content-terms.md), and [content-review skill](../cis-content-review/SKILL.md). Preserve any stronger document-specific authority rule.

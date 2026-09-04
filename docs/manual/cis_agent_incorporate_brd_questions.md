---
title: "cis agent incorporate brd-questions"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-01"
review_cadence: "on command or BRD question workflow change"
cis:
  stable_id: change-impact-studio:manual:cis-agent-incorporate-brd-questions
---

# `cis agent incorporate brd-questions`

Updates the canonical BRD from its complete governed set of human answers.

```text
cis agent incorporate brd-questions --provider <id> --actor <human>
  [--transport <name>] [--timeout-seconds <30..86400>]
  [--approve-requests] [--repo <path>]
  [--format <human|json|agent>]
```

Every recognized Open questions row must have a substantive answer, human actor, and UTC
timestamp. CIS binds the complete normalized table to an answer digest and gives the provider
the exact question IDs and answers as stakeholder authority. The provider must update the
relevant outcomes, scope, actors, requirements, constraints, measures, or traceability rather
than merely leaving decisions in the question table. It may not invent technical design,
expand scope, or reinterpret an answer.

The command runs with `implement` mode and a `workspace-write` ceiling in an isolated Git
scratch repository. Only the canonical BRD may change. CIS rejects the result unless it
returns the exact answer digest and question identities, preserves the Open questions table
verbatim, and leaves protected frontmatter, baseline, source, and feature-traceability blocks
intact. A concurrent canonical edit also rejects copyback. A successful retained candidate
is revalidated and reused rather than paying for another provider run.

On success the run is retained as `PRODUCT/BRD-QUESTION-REVISION`, the one-file result is
copied to the authority repository, and the BRD remains Review Required. Run `cis agent
review brd` through a different provider next. That review is bounded to answer incorporation:
it verifies that every human decision is reflected accurately and reports only omissions,
misinterpretations, or concrete regressions. Approval cannot precede that independent review.

The command is idempotent. A successful applied question revision remains valid when a later
bounded `BRD-REVISION` applies approved review recommendations, because that gate preserves the
governed answer table and the review chain governs the resulting edits. If the current answer
digest and identities still match, repeating this command returns `already-incorporated` and
does not execute a provider. A changed answer set or a newer replacement BRD draft requires a
new incorporation run.

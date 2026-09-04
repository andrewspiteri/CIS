---
title: "cis agent author feature"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-03"
review_cadence: "on feature-authoring or provider-policy change"
cis:
  stable_id: change-impact-studio:manual:cis-agent-author-feature
---

# `cis agent author feature`

Drafts one started high-level feature specification from the current governed product,
technical, architecture, component, experience, standards, security, and testing baseline.

```text
cis agent author feature --item <HLT-ID> --provider <id> --actor <human>
  [--transport <name>] [--timeout-seconds <30..86400>]
  [--approve-requests] [--repo <path>]
  [--format <human|json|agent>]
```

Run `cis brd backlog start --item <HLT-ID>` first. The target specification must remain
`Draft` or `Review Required`; an Active specification cannot be silently rewritten. When
the definition wizard is present, run `cis definition status` and complete its final
consolidated activation before authoring.

CIS creates an isolated Git scratch repository containing only the feature scaffold and
available bounded evidence: the Active BRD, technical intent, overall solution design,
architecture diagrams, component sheet, dictionary index and dictionaries, high-level UI
questionnaire and direction, visual-system preview, backlog, design/API/delivery policy,
applicable repository profiles, standards, test and security profiles, and feature-governance
agent guidance. The source repository and unrelated files are not exposed.

The provider must replace every template placeholder with evidence-backed content or a
reasoned `Not applicable` statement. It expands actors, scenarios, structured `FEAT-*`
requirements and acceptance criteria, workflows and invariants, data and audit needs,
contracts and permissions, UI and accessibility behavior, integration points, lifecycle,
operations, security, and layered verification. It cannot absorb another high-level
outcome or create wireframes, designs, tasks, implementation, approvals, or lifecycle
changes.

Every structured requirement uses the five-column `ID | Surface | Frontend type |
Requirement | Acceptance criteria` schema. `Surface` is limited to `frontend`, `backend`,
`full-stack`, `mobile`, `native`, `api`, `contract`, `data`, `security`, `delivery`, or
`documentation`; `Frontend type` is limited to `public`, `customer`, `backoffice`, or
`not-applicable`. Descriptive boundary names belong in the requirement text.

A result is applied only when the canonical feature remained unchanged, the actual diff
contains exactly that feature, frontmatter is unchanged, every required section remains,
structured feature requirements use the controlled surface/frontend vocabularies, and no
`TODO`, `TBD`, or `TO BE COMPLETED` placeholder remains. Provider completion still does
not prove the feature valid. The controller, rather than the provider, binds the result to
the exact consolidated `product_definition_hash`. A missing or stale binding prevents
validation and approval.

After drafting, review the canonical Markdown, resolve any explicit open questions, run
`cis brd feature validate --item <HLT-ID>`, and approve only through explicit human
authority. Runs retain `<HLT-ID>/FEATURE-DRAFT` provenance under
`.cis/local/agents/runs/`.

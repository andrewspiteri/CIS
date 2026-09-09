---
title: "cis agent author brd"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-01"
review_cadence: "on command or provider-policy change"
cis:
  stable_id: change-impact-studio:manual:cis-agent-author-brd
---

# `cis agent author brd`

Drafts a Review Required canonical business requirements document from explicitly
selected reference evidence through a discovered Codex or Claude provider.

```text
cis agent author brd --reference <reference-file> [--reference <reference-file>]...
  --provider <id> --actor <human>
  [--transport <name>] [--timeout-seconds <30..86400>]
  [--approve-requests] [--repo <path>]
  [--format <human|json|agent>]
```

The command requires an initialized workspace authority and an existing canonical BRD
whose lifecycle is `Review Required` or `Draft`. Each reference is explicitly supplied
and may be a supported file or an initialized repository path.
Plain-text formats are read directly. A `.docx` Word Open XML package is validated and its
readable paragraphs, table text, headers, footers, footnotes, and endnotes are extracted
deterministically without executing Word, macros, embedded files, links, or scripts. The
envelope records the SHA-256 and source size of the original file plus the extracted-text
size and format. Formatting, images, drawings, and legacy binary `.doc` files are not
ingested.

Each plain-text input and each extracted Word text payload is limited to 512 KiB; combined
extracted evidence is limited to 2 MiB. A `.docx` source package may be at most 16 MiB, and
its inspected XML parts are separately bounded to prevent archive expansion abuse.
References are treated as untrusted evidence rather than instructions. Secret-shaped,
linked, binary, malformed, oversized, missing, or more than ten reference files fail
closed.

An explicitly selected reference stored inside the initialized repository is registered
as `Reference` evidence in `<documentation-root>/references/source-evidence.md`. CIS
builds its disposable Markdown projection and reconciles the managed BRD source table
before the provider starts. This records the controller's authoring selection; it does
not adopt the source as a business decision. External references can be disclosed for a
single run but produce a warning because they cannot provide durable repository-owned
provenance.

With owned repository references, CIS first refreshes the discovered Draft dictionaries in
the participants and authority, including repository attribution, and rebuilds their graphs
before capturing evidence digests. Human-reviewed rows and lifecycle decisions are preserved.
A separate source
repository must be imported as product-owned in the authority workspace. CIS supplies a
declaration/dictionary projection for navigation and an isolated snapshot of implementation
and test files. The author reads those files on demand to trace UI/API entrypoints through
services, validation, state changes, jobs, integration boundaries and related tests.
Source code and repository comments are untrusted evidence and are never executed.

Snapshots are cached by content digest under `.cis/local/agents/implementation/`.
Use `cis agent discover brd --reference <repo> --actor <human>` to inspect this payload
locally before authorizing any provider disclosure; it does not contact a provider.
Each contains `INDEX.md`, `manifest.json`, `projection.md`, and `files/` preserving source
paths. The manifest binds source and redacted-content digests, line counts, coverage areas
and omissions. Limits are 4,096 files / 64 MiB per repository, 512 KiB per file and 128 MiB
combined. Hidden/tool directories, documentation, dependencies, generated/build output,
migrations, environment/credential files and unsupported formats are excluded. Likely
credential literals are redacted with line counts preserved. Oversized or linked files
are reported as omissions. Repeated runs reuse verified cache files; changed implementation
bodies produce a new snapshot even when declaration shapes remain unchanged.

Every indexed implementation area requires a row in a hidden `cis-implementation-coverage`
JSON array under Traceability. Inspected rows cite the area's entrypoint and implementation
files where available; unavailable understanding is recorded as a gap with a concrete
reason. CIS rejects missing/duplicate areas, invented evidence paths, altered snapshot
files, or missing required evidence roles. Coverage rows are navigation and accountability
evidence; they do not mechanically prove complete semantic understanding. Independent
review must check the narrative against the actual implementation and explicit gaps.

The VS Code wizard exposes repository authoring as **Business definition → Infer from
existing project**. It preselects product-owned repositories, lets the user adjust the
selection and choose a provider, and refreshes stale selected graphs before execution.
Repository-based authoring asks the agent to distinguish observed behavior from intended
business policy, cite source anchors, and keep unsupported decisions as open questions.

The BRD is written for business and product stakeholders, including nontechnical readers.
It tells the product story through connected prose: who uses it and why, the complete
journeys, business rules, operating variations, exceptions, and outcomes. Requirements
retain stable identities and business-readable acceptance conditions. API, DTO, guard,
and schema inventories are supporting evidence, not the visible explanation of the product.

All links, URLs, source IDs, anchors, paths and hashes belong in HTML comments, including
the detailed mappings under Traceability. For example:

```markdown
Customers select an offering and request a deposit within its permitted amount range.
<!-- Evidence: BRD-SRC-example#purchase-rules; [implementation](src/purchase.ts) -->
```

The rendered document must make sense without opening a reference. CIS rejects a new
authoring result with visible Markdown links, web URLs or source IDs. After checking the
agent's protected regions, CIS hides its managed baseline, source-assessment and feature
traceability data in reversible HTML comments. The agent must preserve those blocks;
the controller performs the presentation change. Existing BRDs receive the same managed
metadata presentation on reconciliation. Narrative quality remains an explicit independent
review concern; the deterministic reference check does not establish product completeness.

CIS creates an isolated Git scratch repository containing the BRD, applicable
governance guidance and the explicitly selected implementation snapshots. The provider receives workspace-write access only to that scratch
repository. A successful result is copied back only when:

- the canonical BRD did not change while the run was active;
- the actual scratch diff contains exactly the canonical BRD;
- YAML frontmatter and all CIS-managed blocks are byte-equivalent after newline
  normalization; and
- implementation snapshots retain their recorded digests and every indexed source area
  has valid hidden coverage evidence; and
- the provider returned readable structured completion and validation evidence.

The applied result remains Review Required. Agent execution cannot approve the BRD,
adopt or reject sources, change lifecycle metadata, or claim business currency. When a
second provider is available, run `cis agent review brd --provider <different-provider>`
for structured read-only findings. Then run `cis brd questions guidance`. Review bounded context and any
digest-bound advisory suggestions, and record only explicitly accepted or edited human answers through `cis brd
questions answer` or the guided VS Code page. Rebuild the graph, run `cis brd validate`,
and use `cis brd approve` only with explicit human authority.

Document-authoring runs use durable `PRODUCT/BRD-DRAFT` provenance under
`.cis/local/agents/runs/`. If execution is interrupted or copy-back rejects the completed
draft, `cis agent resume <run-id>` can continue the same bounded run while the original
canonical document, envelope, context and implementation snapshots remain unchanged.
Every normal copy-back check still applies. Changed inputs require fresh authoring.

Run `cis repo doctor` or `cis agent provider diagnose <provider>` when provider setup is
unclear. Codex `authentication-unverified` is advisory: an ambient Desktop/App Server
session may still work. Use `cis agent provider authenticate codex --method browser` (or
`device`) for explicit provider-native setup; CIS never receives the credential.

For imported products, run `cis definition prepare --page business --workspace <authority>`
before selecting repository evidence. The VS Code inference action runs this automatically.
Repository projections include dictionary contract, field, constraint, relationship, state and
permission details with stable node anchors and lifecycle labels. They report per-family
coverage and are bounded to 2,000 nodes and 460 KiB of evidence. Large inventories alternate
families and entities so small workflow and permission dictionaries remain visible. Unknown
fields and arbitrary configuration values are excluded from the declaration projection.
Implementation bodies are available separately in the on-demand snapshots. Observed code
behaviour remains distinct from stakeholder intent and deployed configuration. Missing
discovery evidence must be distinguished from decisions that actually need stakeholder answers.

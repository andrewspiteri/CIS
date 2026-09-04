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

An initialized repository reference requires a fresh CIS graph. CIS supplies the agent
a bounded projection of repository classification, components, contracts, behavior,
relationships, and source routes with stable graph-node anchors. It does not concatenate
the source tree. A separate source repository must already be imported into the
authority workspace.

CIS creates a minimal isolated Git scratch repository containing the BRD and applicable
governance guidance. The provider receives workspace-write access only to that scratch
repository. A successful result is copied back only when:

- the canonical BRD did not change while the run was active;
- the actual scratch diff contains exactly the canonical BRD;
- YAML frontmatter and all CIS-managed blocks are byte-equivalent after newline
  normalization; and
- the provider returned readable structured completion and validation evidence.

The applied result remains Review Required. Agent execution cannot approve the BRD,
adopt or reject sources, change lifecycle metadata, or claim business currency. When a
second provider is available, run `cis agent review brd --provider <different-provider>`
for structured read-only findings. Then run `cis brd questions guidance`. Review bounded context and any
digest-bound advisory suggestions, and record only explicitly accepted or edited human answers through `cis brd
questions answer` or the guided VS Code page. Rebuild the graph, run `cis brd validate`,
and use `cis brd approve` only with explicit human authority.

Document-authoring runs use durable `PRODUCT/BRD-DRAFT` provenance under
`.cis/local/agents/runs/`. They cannot be resumed because a fresh run must bind the
current canonical draft and current reference digests.

Run `cis repo doctor` or `cis agent provider diagnose <provider>` when provider setup is
unclear. Codex `authentication-unverified` is advisory: an ambient Desktop/App Server
session may still work. Use `cis agent provider authenticate codex --method browser` (or
`device`) for explicit provider-native setup; CIS never receives the credential.

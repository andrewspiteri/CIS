---
title: "CIS File Index Card Specification"
type: repository-specification
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-09"
review_cadence: "on index or AI policy change"
cis:
  stable_id: change-impact-studio:spec:file-index-cards
---

# File Index Card Specification

## Purpose

CIS file index cards are low-token routing summaries for repository files. They help
an agent identify likely source, test, configuration, and documentation files before
broad search or large source reads. They adapt PARR's document-card and code-area-card
pattern to an incremental per-file index.

Cards are never source of truth. The source file, canonical Markdown, governed
reference, and validated graph retain their existing authority.

## Storage and authority

Generated cards are disposable derived state:

```text
.cis/local/index-cards/
  README.md
  index.json
  cards/<path-hash>.card.md
```

They are not added to the documentation catalog or committed as canonical Markdown.
`README.md` is a human-browsable index; `index.json` is the machine-readable routing
index. A card records source path and hash, provider, model, prompt version, local or
remote execution, input truncation, sensitive-content handling, and generation time.

Maintainers may still create curated, canonical routing cards beneath the selected
documentation root. Those authored cards follow normal catalog and review rules and
must not be overwritten by `cis index`.

## Eligibility and safeguards

Indexing covers supported text source, test, project, infrastructure, configuration,
and Markdown formats. Generated/vendor directories such as `.git`, `bin`, `obj`,
`node_modules`, `.godot`, `.gradle`, and `.cis/local` are excluded. Binary content is
not submitted to a model.

Generated Android and Gradle trees beneath `android/build/` and `plugin/build/` are
excluded. Repository-owned root `build/` tooling and `games/<game>/build/` scripts
remain eligible because those paths commonly contain canonical build orchestration.

Likely credential files, including `.env`, private keys, certificates, and names that
identify secrets, credentials, passwords, or tokens, receive only a deterministic
safeguard card. Their contents are never submitted to any provider.

Per-file model input is bounded and may be truncated. Summaries are normalized to one
plain-text routing sentence of at most 240 characters, regardless of whether a small
model follows the prompt exactly. Prompt version `cis-file-index-card-v3` also rejects
an unsupported Unity claim when that technology does not occur in the source path or
submitted content, replacing it with conservative file-type routing text.

## Provider policy

`cis index build` auto-selects only an available local provider. The first provider is
Ollama, discovered through `OLLAMA_HOST` or `http://127.0.0.1:11434`; the smallest
reported local model is selected unless `--model` is supplied.

An OpenAI-compatible inexpensive endpoint may be configured with
`CIS_AI_ENDPOINT`, `CIS_AI_MODEL`, and optionally `CIS_AI_API_KEY`. It is never selected
automatically. The command requires both `--provider openai-compatible` and
`--allow-remote`, making repository-content transmission explicit.

Provider/model choice is provenance, not authority. Model text cannot create graph
relationships, validate implementation, approve documents, or replace human review.

## Incremental behavior

Source content hash, provider, model, and prompt version form the reuse key. Unchanged
cards are retained byte-for-byte. `--limit` bounds new or refreshed model calls so a
large repository can be indexed in inexpensive batches. `--path` scopes a run without
deleting cards outside that file or directory. Deleted in-scope files remove only
their derived cards.

`cis index status` compares current source hashes with cached cards without invoking a
model. `cis index find` searches cached paths and summaries without invoking a model.

## Routing workflow

1. Run `cis index status`.
2. Build bounded batches until missing/stale coverage is acceptable.
3. Run `cis index find --text <task terms>` before broad source search.
4. Open returned source files and verify authoritative behavior.
5. Use graph/context commands for relationships, contracts, callers, and tests.

Index-card matches are orientation evidence only. Absence of a match does not prove
that a file or impact is irrelevant.

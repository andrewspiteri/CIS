---
title: "cis context pack"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-context-pack
---

# `cis context pack`

Creates a deterministic, budget-bounded Markdown context pack from an exact graph
target and its canonical source locations. Packs are disposable derived state beneath
`.cis/local/context/`; Markdown and implementation files remain canonical.

## Synopsis

```text
cis context pack --id <id> [--kind <kind>] [--purpose <text>]
  [--root <repository-path>#<node-id>[#<kind>]]...
  [--verification <text>] [--depth <number>] [--limit <number>]
  [--max-chars <number>] [--include-proposed] [--output <path>] [--force]
  [--repo <path-or-registered-id>] [--workspace <path>]
  [--format <human|json|agent>]
```

The command resolves one exact start node, performs a cycle-safe bounded traversal,
and selects readable repository sources using deterministic priority rules. Every pack
records its purpose, requested verification, graph build and baseline, source-selection
reasons, strict versus interpreted evidence, omissions, diagnostics, and truncation.
Deterministic declared, discovered, and confirmed evidence is classified as `strict`;
proposal or possibly-stale evidence is classified as `interpreted`.

Precise `line`, `heading`, `table-row`, and `declaration` graph locators select bounded
excerpts. Whole-file selection is used only when no precise locator can be resolved.
Every selected source records its node keys, edge type and state, confidence, locator,
and extraction method. The Markdown manifest reports source size, locator-selected
characters, included characters, and an estimated token count using four characters
per token.

`--id` identifies the primary root in the primary `--repo`. Each repeatable `--root`
adds another exact root using `<repository-path>#<node-id>[#<kind>]`. Use `.` as the
repository path for another root in the primary repository. Relative repository paths
are resolved from the primary repository. CIS federates up to 20 independently built
graphs at query time and preserves each repository's identity, graph build, freshness,
diagnostics, paths, and evidence. It does not invent cross-repository edges.

Candidate selection is interleaved by relevance rank across roots, preventing the
first repository from consuming the source budget before later roots contribute. The
pack is always written beneath the primary repository's `.cis/local/context/`; source
repositories remain read-only.

With `--workspace`, `--repo` is required and selects the primary repository by its
registered ID. The repository portion of every `--root` may also be a registered ID.
The workspace registry removes path repetition but does not change graph evidence or
create relationships.

The character budget covers embedded excerpts. CIS assigns a bounded per-source share
before consuming the remaining global budget so one highly connected file cannot crowd
out every other source. An excerpt that crosses either bound is included partially and
marked truncated; subsequent sources are listed as omitted. Duplicate traversal evidence
is compacted, with total, shown, and omitted counts preserved in the pack.

## Options

| Option | Required | Default | Effect |
| --- | --- | --- | --- |
| `--id <id>` | Yes | — | Selects the exact graph key or node-local ID used as the pack root. |
| `--kind <kind>` | No | — | Disambiguates a node-local ID. |
| `--root <spec>` | No | — | Adds an exact same- or cross-repository root as `<repository-path>#<node-id>[#<kind>]`; repeat up to 19 times. |
| `--purpose <text>` | No | `engineering change` | Records the task or change the pack supports. |
| `--verification <text>` | No | `connected tests and deterministic checks` | Records the requested verification evidence. |
| `--depth <number>` | No | `2` | Limits traversal depth to 1–10. |
| `--limit <number>` | No | `100` | Limits graph nodes to 1–1000. |
| `--max-chars <number>` | No | `50000` | Limits embedded source content to 1000–1000000 characters. |
| `--include-proposed` | No | `false` | Includes proposed relationships; rejected edges remain excluded. |
| `--output <path>` | No | Content-addressed name | Selects a `.md` file beneath `.cis/local/context/`, or a file name in that directory. |
| `--force` | No | `false` | Replaces different content at the selected derived output path. |
| `--repo <path-or-id>` | Conditional | Current directory | Selects the primary initialized repository; a registered ID is required with `--workspace`. |
| `--workspace <path>` | No | — | Resolves primary and additional repository IDs from `.cis/workspace.yml`. |
| `--format <format>` | No | `human` | Selects `human`, `json`, or `agent` command output. |

## Effects and safety

- Reads the stored graph and repository sources without rebuilding them.
- Requires every participating repository to be initialized with a built local graph;
  a missing or ambiguous additional root fails the pack rather than silently omitting it.
- Writes one Markdown artifact beneath `.cis/local/context/` using an atomic replace.
- Returns `unchanged` when the same content already exists.
- Rejects output outside `.cis/local/context/`, including attempts to overwrite
  canonical documentation, source, or graph-generation files.
- Rejects known credential/key paths, binary content, files larger than 2 MB, private
  keys, recognized access tokens, JWTs, and likely literal credentials. Omission
  reasons name the detection rule but never echo the matched value.
- Does not include proposed edges unless explicitly requested.
- Classifies sources reached through proposed or possibly-stale evidence as
  `interpreted`; all deterministic or reviewed evidence remains `strict`.

## Exit codes

| Code | Meaning |
| ---: | --- |
| `0` | Pack created, unchanged, or no matching target found without structural errors. |
| `2` | Invalid repository context, bounds, identity, format, or output path. |
| `4` | Graph unavailable or a differing output already exists without `--force`. |
| `5` | Graph or source diagnostics contain an error. |

## Related commands

- [`cis context search`](cis_context_search.md)
- [`cis graph build`](cis_graph_build.md)
- [`cis graph related`](cis_graph_related.md)

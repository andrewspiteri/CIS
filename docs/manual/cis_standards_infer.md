---
title: "cis standards infer"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-16"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-standards-infer
---

# `cis standards infer`

Matches applicable known patterns against a fresh compiler-backed repository graph and produces non-canonical inferred-standard candidates.

## Synopsis

```text
cis standards infer [--repo <path>] [--pattern <id>...] [--language <name>...] [--component <id>...] [--evidence-limit <1..100>] [--format <human|json|agent>]
```

Without `--pattern`, every applicable pattern is considered. `--component` restricts eligible subject nodes without changing the pattern. `--evidence-limit` bounds matching and counterexample rows per evaluation and defaults to 20.

Inference stops when the graph is missing or stale. Ready patterns are evaluated deterministically; patterns that require unavailable compiler facts remain visible as `requires-graph-capability`. Passing thresholds creates an `Unreviewed` candidate. It does not create, activate, approve, or modify a standard.

Generated compiler nodes are excluded from eligible populations unless the selected
pattern explicitly requests the `generated` facet. Invocation evidence stores argument
shapes rather than raw values. Dataflow matches use bounded source-order milestones
inside one callable symbol and must not be presented as branch-sensitive,
interprocedural, or runtime proof.

Built-in patterns select graph identities and classified components rather than assuming
source lives beneath `src/` or tests beneath `tests/`. This supports conventional .NET
test projects, game repositories, and other valid repository layouts without repository-
specific pattern copies.

Derived evidence is written to:

```text
.cis/local/standards/inference/report.json
.cis/local/standards/inference/report.md
```

Each result records the graph build, eligible and matching counts, consistency, matches, counterexamples, proposed wording, and suggested enforcement. Markdown remains canonical; these local reports may be deleted and rebuilt.

Exit codes are `0` for completed inference, `2` for invalid options or pattern selection, `4` for unavailable graph state, and `5` for stale or otherwise unsafe graph evidence.

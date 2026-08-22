---
title: "cis index build"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-index-build
---

# `cis index build`

Builds or refreshes content-hashed, non-authoritative routing cards beneath
`.cis/local/index-cards/`.

```text
cis index build [--repo <path>] [--path <relative-file-or-directory>]
  [--provider <name>] [--model <name>] [--allow-remote]
  [--limit <0-100000>] [--max-input-chars <1000-100000>]
  [--refresh] [--format <human|json|agent>]
```

Auto-selection considers local providers only and chooses the smallest reported local
model. `--limit 0` means unlimited model calls; a positive value supports inexpensive
incremental batches. `--refresh` invalidates otherwise reusable matching cards.

Each routing-summary generation is capped at 160 output tokens with a 60-second
request timeout. During generation, the command checkpoints the index manifest after
every ten newly generated cards. If a first run is interrupted, rerunning the command
reuses the last checkpoint and regenerates at most the nine cards produced after it.
Completed content-addressed cards are also retained when a later call fails, so a
retry resumes the remaining work without regenerating unchanged cards.

The first model-backed run can take significant time and memory on a large repository;
the exact cost depends on the selected model and local provider. Later runs compare
content hashes and normally reuse unchanged cards. Use a positive `--limit` to split
the initial generation into explicit batches when local capacity is constrained.

Likely sensitive files receive deterministic cards without content submission.
Generated Android/plugin `build/` trees are excluded while canonical root and
game-owned build scripts remain eligible. Unsupported Unity claims without source
evidence are replaced with conservative routing text.
OpenAI-compatible remote use requires explicit provider selection and
`--allow-remote`. Exit `0` includes complete, incomplete, and unchanged safe builds;
exit `2` means invalid input; exit `4` means provider or remote authorization is
unavailable; exit `5` means one or more card generations failed.

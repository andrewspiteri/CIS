---
title: "cis design reuse"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-25"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-design-reuse
---

# `cis design reuse`

Carries exact PNG artifacts from an approved earlier change into a target wireframe
screen without regenerating or copying the image files.

```text
cis design reuse <change-id> --source-change <change-id> --source-screen <screen-id> --target-screen <screen-id> --reason <compatibility-rationale> [--repo <path>] [--format <human|json|agent>]
```

The target pack must still be `Inactive`. The source design must be approved, and its
wireframe, renderer, PNG manifest, files, dimensions, and hashes must still match the
recorded approval. The target screen must exist in the current `wireframes.md`.

The command is idempotent for the same source-change, source-screen, and target-screen
mapping. It writes one canonical row per source PNG to `design.md`, including source
approval digests, a separately calculated current wireframe-content digest, and a
compatibility rationale. Keeping both wireframe digests preserves authority created by
older CIS hashing versions while still detecting any later source edit. It does not
create new human authority.

Run this command for every approved source state that satisfies the target screen before
`cis design scaffold`. Scaffolding omits target screens with valid reuse evidence, so the
renderer contains only visual gaps. `cis design render`, `validate`, and `approve` then
use a combined manifest of reused and newly rendered PNGs. Any later source drift fails
closed.

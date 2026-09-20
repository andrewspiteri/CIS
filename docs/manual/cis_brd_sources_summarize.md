---
title: "cis brd sources summarize"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-11"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-brd-sources-summarize
---

# `cis brd sources summarize`

Creates short document summaries for reviewing BRD source decisions. The Business
Definition wizard displays each summary beneath a document-title link that opens a
pinned editor tab beside the wizard. Summary generation does not assess sources,
change the business narrative, answer questions, or approve the BRD.

```text
cis brd sources summarize [--workspace <path>] [--limit <1-100>] [--format <human|json|agent>]
```

The default limit is 100 missing summaries. CIS selects an available local text model
only. There is no remote-provider or allow-remote option. The command is cancellable
from VS Code; already completed summaries remain cached.

Normal `brd status` and `definition status` never invoke the model. They return a cached
local-model summary when it matches the current source content, or a document excerpt
when no current summary exists. The wizard's **Summarize documents locally** action
generates missing summaries separately from startup and preserves unfinished form edits.

Summaries are disposable files beneath the authority's
`.cis/local/brd/source-summaries/`, with one cache entry per source identity. Content
changes invalidate the old summary. A corrupt cache falls back to an excerpt. Generation
checks the source again before caching and rejects generated wording with missing or
mismatched supporting document quotes. If the model cannot produce a usable summary,
CIS asks it to select up to two original document sentences instead. Document metadata
is excluded from that selection. Quotes are validation evidence; the visible summary stays
short and readable. A local-model summary is an aid to review, not proof of correctness
or implementation.

Text summaries support Markdown and plain text up to 2 MiB. The model receives at most
roughly 4,500 characters of cleaned document text, using beginning, middle and ending
excerpts for longer sources. The UI labels summaries based on selected excerpts. Code
fences and HTML comments are excluded. Suspected credential material is not submitted.
Repository-level evidence and unsupported formats show an explanation instead.

If the local model is unavailable or returns unusable output, existing summaries and
excerpts remain visible and the command reports warnings. Exit `0` means the pass
completed (possibly with warnings); invalid workspace or limit requests return `2`.

## Related commands

- [`cis brd sources assess`](cis_brd_sources_assess.md)
- [`cis brd status`](cis_brd_status.md)
- [`cis definition status`](cis_definition_status.md)

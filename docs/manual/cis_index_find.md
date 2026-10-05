---
title: "cis index find"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-10-05"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-index-find
---

# `cis index find`

Searches cached source paths and routing summaries. It does not invoke a model or
claim that matching files are the complete impact set.

```text
cis index find (--text <terms> | --query <terms>) [--limit <1-1000>]
  [--repo <path>] [--format <human|json|agent>]
```

Path matches rank above summary-only matches. Open returned source files for
authoritative detail, then use graph/context commands for relationships. Exit `0`
includes no-match; exit `2` means invalid input; exit `4` means the index is missing;
exit `5` means the stored index could not be queried.

## Option compatibility

`--query` is an alias of `--text`. New scripts and skills should use `--text`.
Parser errors are recorded as invalid requests, not execution failures.

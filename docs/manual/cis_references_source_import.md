---
title: "cis references source import"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-30"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-references-source-import
---

# `cis references source import`

Register one repository-owned source document or initialized repository and build its deterministic local Markdown projection.

```text
cis references source import --file <path> --assessment <Unreviewed|Reference|Adopted|Rejected> --actor <identity> --reason <rationale> [--repo <path>] [--format <human|json|agent>]
```

The command updates `<documentation-root>/references/source-evidence.md` and writes derived files beneath `.cis/local/references/<BRD-SRC-ID>/`. A changed file or repository graph does not silently advance its registered digest. `.docx`, `.md`, `.txt`, and initialized repository paths are supported. A repository requires a fresh graph and, when separate from the authority, workspace registration. Repository projection uses classified graph evidence and stable node anchors; it does not dump the source tree.

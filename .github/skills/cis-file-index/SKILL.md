---
name: cis-file-index
description: Build, refresh, and query low-token per-file CIS routing cards. Use before broad repository searches, when locating likely implementation or documentation files, or when repository changes may have made cached routing summaries stale.
---

# CIS File Index

1. Run `cis index status --format agent`.
2. If the index is missing or stale, run `cis index build --limit 100 --format agent`; repeat bounded batches until pending coverage is zero.
3. Use `cis index find --text <task terms> --format agent` before broad file searches.
4. Open the reported source files, not only their cards, before making factual claims or changes.
5. Rebuild changed cards after implementation and report provider, model, truncation, pending files, and errors.

Cards under `.cis/local/index-cards/` are disposable, non-authoritative routing aids. Auto-selection may use local Ollama only. Never use `--allow-remote` unless the user explicitly authorizes transmitting the selected repository content, and never submit sensitive files.

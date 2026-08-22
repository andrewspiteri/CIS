---
title: "Visual Studio Code Thin Client"
type: specification
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-14"
review_cadence: "on editor or CLI contract change"
cis:
  stable_id: change-impact-studio:spec:vscode-client
---

# Visual Studio Code thin client

The extension under `vscode-extension/` is presentation and navigation only. It reads the
initialized documentation root, opens canonical Markdown with VS Code's built-in preview,
queries read-only JSON commands, and launches visible CIS tasks for mutating or long-running
commands.

The extension must not implement graph construction, planning, provider routing, tracker
reconciliation, workflow execution, agent ingestion, verification, approvals, diagnostics,
or learning logic. It passes arguments as a process argument array rather than a shell
string. `cis.executablePath` is a single executable path; users needing a composite launch
must provide a wrapper executable.

The client never reads credentials, `.env` files, model prompts, tracker bodies, or sensitive
diagnostic sources. CLI exit codes, JSON output, canonical Markdown, and `.cis/local/` state
remain authoritative for display.

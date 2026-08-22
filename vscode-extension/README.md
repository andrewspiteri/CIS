# Change Impact Studio for Visual Studio Code

This extension is a thin client over the `cis` CLI. It contains no planning, graph,
tracker, agent, verification, approval, or learning engine logic.

It provides:

- a repository tree for canonical Markdown overview, changes, references, and manuals;
- Markdown preview through VS Code's built-in renderer;
- visible tasks for mutating or long-running CIS commands;
- read-only JSON queries for tracker and AI status;
- a status-bar entry for repository health.

Set `cis.executablePath` when `cis` is not on `PATH`. The value must be an executable,
not a shell command. CLI authority, exit codes, profiles, credentials, `.cis/local/`
state, and Markdown records remain unchanged.

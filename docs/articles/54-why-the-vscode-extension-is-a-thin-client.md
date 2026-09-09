---
title: "Why the VS Code Extension Is a Thin Client"
type: article
status: Draft
series: "Building Change Impact Studio"
series_order: 4
owner: "Andrew Spiteri"
last_reviewed: "2026-09-09"
review_cadence: on editor-client change
summary: "The editor presents canonical Markdown and CLI JSON without creating a second product implementation."
cis:
  stable_id: change-impact-studio:article:vscode-thin-client
---

# Why the VS Code extension is a thin client

An editor extension can easily become a second application with its own state,
validation, and interpretation of lifecycle. CIS avoids that split.

## The CLI owns product behavior

The extension invokes stable CLI commands with argument arrays and consumes structured
JSON. Repository initialization, product definition, context, impact, planning, agent
execution, evidence reconciliation, verification, and authority rules remain in .NET
modules.

## Markdown remains the review surface

Workspace, Journey Map, Changes, Evidence, Runs, and Governance views route users to
canonical repository documents and CLI-owned state. Preview improves navigation but does
not move content into extension storage.

## Thin clients reduce drift

Terminal users, agents, CI, and the extension receive the same validation and exit
semantics. A product change needs one domain implementation.

## The client still owns experience

The extension can manage multi-root authority selection, Welcome and getting-started
flows, activity views, command invocation, coalesced refresh, progress, error display,
safe Markdown preview, and bounded webviews. The product-definition wizard is a deliberate
onboarding projection across eight CLI-owned pages; it is not a second definition engine.

Provider execution follows the same rule. The client displays CIS-supplied eligibility,
target, transport, permission ceiling, isolation, events, and result state. Importing a
result imports evidence only; the extension cannot approve scope or completion.

## One state machine is enough

If the extension kept its own plan, approval, or run lifecycle, terminal and editor users
could observe different truth. Offline edits to Markdown might not reach extension state;
an extension upgrade could reinterpret an accepted record. A thin client queries current
CLI and repository state instead of maintaining a parallel authority database.

Local UI preferences—selected authority, expanded tree nodes, panel placement—can remain
client state because losing them does not change product meaning.

## Invoke commands safely

The extension passes executable arguments as data and consumes structured output. It does
not construct opaque shell strings or parse human prose. Standard error carries
diagnostics; JSON carries the result. Timeouts, cancellation, invalid output, and non-zero
exit codes become explicit UI states.

Command previews and confirmations should show the same planned mutation the CLI would
apply. A convenient button cannot bypass collisions, untrusted workspace checks, or
human-review gates.

## Multi-root workspaces need explicit authority

A VS Code workspace can contain several folders and even several CIS products. The client
must never select authority by focus, alphabetical order, or last command implicitly. It
asks the maintainer to choose, persists that local preference, and displays the current
product and ecosystem identity prominently.

Owned participants and dependencies keep their configured boundaries. Opening a
dependency source file does not make it a write target for the active product.

## Webviews remain bounded projections

The product-definition wizard and other rich panels render CLI-owned page status and
canonical artifacts. Save and Prepare actions invoke the owning commands, then refresh
from their results. The panel should not invent lifecycle transitions or keep unsaved
authority hidden in browser state.

Markdown preview is treated as untrusted content: scripts do not gain repository access,
links and file navigation remain contained, and the workspace trust boundary is respected.

## Runs are observable, not self-approving

The Runs view can select a provider, show eligibility, stream normalized events, cancel
an owned process, resume a supported attempt, and present result evidence. Import remains
an explicit action and still does not complete the task. The editor makes the boundary
understandable without weakening it.

## Test the packaged experience

Extension unit tests cover command construction, output parsing, coalesced refresh, and
view behavior. VSIX smoke tests use a clean profile to prove activation, CLI discovery,
version compatibility, initialized and uninitialized states, getting started, and
Repository Doctor. Source tests cannot prove the installed boundary works.

## Design for graceful absence

The client should remain useful when no folder is open, the workspace is untrusted, CIS
is not installed, a repository is uninitialized, or a command returns invalid JSON. It can
explain the current boundary and offer safe next steps without fabricating product state.

This also improves accessibility and support: users see actionable command identity and
diagnostics rather than a view that is merely empty.

## Takeaway

Use the editor to make governed state visible and convenient. Keep the domain in the
CLI so every execution surface shares one contract.

## Canonical CIS sources

- [Visual Studio Code client](../specs/vscode-client-spec.md)
- [System context](../specs/system-context-spec.md)
- [Technical intent](../specs/technical-intent-spec.md)

---
title: "Why the VS Code Extension Is a Thin Client"
type: article
status: Draft
series: "Building Change Impact Studio"
series_order: 4
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: on editor-client change
summary: "The editor presents canonical Markdown and CLI JSON without creating a second product implementation."
cis:
  stable_id: change-impact-studio:article:vscode-thin-client
---

# Why the VS Code extension is a thin client

An editor extension can easily become a second application with its own state,
validation, and interpretation of lifecycle. CIS avoids that split.

## The CLI owns product behavior

The extension invokes stable CLI commands and consumes structured JSON. Repository
initialization, impact, planning, verification, and authority rules remain in .NET modules.

## Markdown remains the review surface

Trees and commands route users to canonical repository documents. Preview improves
navigation but does not move content into extension storage.

## Thin clients reduce drift

Terminal users, agents, CI, and the extension receive the same validation and exit
semantics. A product change needs one domain implementation.

## The client still owns experience

The extension can manage activity views, command invocation, progress, error display,
and safe Markdown preview. Those are client concerns, not change-domain authority.

## Takeaway

Use the editor to make governed state visible and convenient. Keep the domain in the
CLI so every execution surface shares one contract.

## Canonical CIS sources

- [Visual Studio Code client](../specs/vscode-client-spec.md)
- [System context](../specs/system-context-spec.md)
- [Technical intent](../specs/technical-intent-spec.md)


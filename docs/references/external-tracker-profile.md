---
title: "Change Impact Studio External Tracker Profile"
type: external-tracker-profile
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-14"
review_cadence: "on tracker, project, workflow, or credential-source change"
cis:
  stable_id: change-impact-studio:reference:external-tracker-profile
---

# Change Impact Studio external tracker profile

No production tracker is enabled for this repository. Enable a provider only after
loading its adapter and reviewing the exact target, mappings, credential source, and
`cis tracker plan` output. Credentials must never be stored here.

| Provider | Kind | Enabled | Target | Base URL | Direction | Issue type | Credential source |
|---|---|---|---|---|---|---|---|
| github | github | no | owner/repository | https://api.github.com | cis-to-remote | issue | environment:GITHUB_TOKEN |
| jira | jira | no | PROJECT | https://example.atlassian.net | cis-to-remote | Task | basic-environment:JIRA_EMAIL:JIRA_API_TOKEN |

The built-in GitHub transport uses the GitHub REST API and the built-in Jira transport
uses Jira Cloud REST API v3 with Atlassian document format descriptions. Enabling a row
is an explicit remote-write decision. The named environment variables must be supplied
by the invoking process and are never copied into CIS state or Markdown.

## Field and lifecycle mappings

| Provider | CIS field | Remote field | Direction | Required | Notes |
|---|---|---|---|---|---|
| github | title | title | CIS to remote | yes | Canonical task title wins after reviewed reconciliation. |
| github | projected body | body | CIS to remote | yes | Contains stable identity and canonical path. |
| jira | title | summary | CIS to remote | yes | Canonical task title wins after reviewed reconciliation. |
| jira | projected body | description | CIS to remote | yes | Adapter selects the supported Jira document format. |

## Provider-specific status mappings

| Provider | CIS status | Remote status | Remote transition | Authority |
|---|---|---|---|---|
| github | repository-defined | repository-defined | repository-defined | CIS remains authoritative. |
| jira | repository-defined | repository-defined | repository-defined | CIS remains authoritative. |

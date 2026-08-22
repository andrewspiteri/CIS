---
title: "Change Impact Studio Diagnostics Evidence Profile"
type: diagnostics-profile
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-14"
review_cadence: "on runtime evidence source or sensitivity change"
cis:
  stable_id: change-impact-studio:reference:diagnostics-profile
---

# Change Impact Studio diagnostics evidence profile

Sources are disabled until their repository-relative sanitized export exists.

| Source | Kind | Location | Enabled | Sensitive |
|---|---|---|---|---|
| application-log | text-log | .cis/local/diagnostics/input/application.log | no | no |
| test-log | text-log | .cis/local/diagnostics/input/tests.log | no | no |

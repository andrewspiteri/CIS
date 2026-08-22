---
title: "Standard Delivery Verification Workflow"
type: workflow-definition
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-14"
review_cadence: "on build or test command change"
cis:
  stable_id: change-impact-studio:workflow:standard-delivery
---

# Standard delivery verification workflow

CIS executes these arguments without a shell and checkpoints each step under
`.cis/local/`. This definition is specific to the CIS .NET solution.

| Step | Command | Depends on | Continue on failure | Timeout seconds |
|---|---|---|---|---:|
| build | dotnet build ChangeImpactStudio.slnx --no-restore | - | no | 1200 |
| test | dotnet test ChangeImpactStudio.slnx --no-build --no-restore | build | no | 1800 |

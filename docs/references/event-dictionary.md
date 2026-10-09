---
title: "Event Dictionary"
type: reference
status: Draft
owner: Repository maintainer
review_cadence: on change
cis:
  stable_id: change-impact-studio:reference:event-dictionary
---

# Event Dictionary

Governed by `change-impact-studio:spec:event-dictionary`. CIS seeded 7 row(s) from deterministic repository evidence. Review them before changing this document to Active.

| Event ID | Event name | Event type | Producer | Consumers | Triggering command | Payload summary | Ordering / idempotency | Sensitivity | Status | Evidence |
|---|---|---|---|---|---|---|---|---|---|---|
| EVT-CIS-ABSTRACTIONS-CIS-AGENT-PROVIDER-EVENT | CisAgentProviderEvent | unknown | cis-abstractions | unknown | unknown | unknown | unknown | unknown | Draft | src/Cis.Abstractions/CisAgentProviderContracts.cs |
| EVT-CIS-MODULES-AGENT-AGENT-RUN-EVENT | AgentRunEvent | unknown | cis-modules-agent | unknown | unknown | unknown | unknown | unknown | Draft | src/Cis.Modules.Agent/AgentService.cs |
| EVT-CIS-MODULES-BRD-STORY-TASK-EVENT | StoryTaskEvent | unknown | cis-modules-brd | unknown | unknown | unknown | unknown | unknown | Draft | src/Cis.Modules.Brd/FeatureIntakeService.StoryWorkflow.cs |
| EVT-CIS-MODULES-DIAGNOSTICS-DIAGNOSTIC-EVENT | DiagnosticEvent | unknown | cis-modules-diagnostics | unknown | unknown | unknown | unknown | unknown | Draft | src/Cis.Modules.Diagnostics/DiagnosticsService.cs |
| EVT-CIS-MODULES-REFERENCES-TESTS-LIST-CREATED-EVENT | ListCreatedEvent | unknown | cis-modules-references-tests | unknown | unknown | unknown | unknown | unknown | Draft | tests/Cis.Modules.References.Tests/ReferenceGovernanceTests.cs |
| EVT-CIS-MODULES-REPOSITORY-TESTS-ORDER-CREATED-EVENT | OrderCreatedEvent | unknown | cis-modules-repository-tests | unknown | unknown | unknown | unknown | unknown | Draft | tests/Cis.Modules.Repository.Tests/RepositoryInitializerTests.cs |
| EVT-CIS-MODULES-REPOSITORY-TESTS-PROGRESS-EVENT | ProgressEvent | unknown | cis-modules-repository-tests | unknown | unknown | unknown | unknown | unknown | Draft | tests/Cis.Modules.Repository.Tests/RepositoryGuidanceReviewRunnerTests.cs |

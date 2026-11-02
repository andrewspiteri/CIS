---
title: "Data Dictionary"
type: reference
status: Draft
owner: Repository maintainer
review_cadence: on change
cis:
  stable_id: change-impact-studio:reference:data-dictionary
---

# Data Dictionary

Governed by `change-impact-studio:spec:data-dictionary`. CIS seeded 2 row(s) from deterministic repository evidence. Review them before changing this document to Active.

| Entity | Field | Type | Required | Constraints | Sensitive | Owner | Lifecycle | Status | Evidence |
|---|---|---|---|---|---|---|---|---|---|
| Order | * | entity | unknown | unknown | unknown | cis-modules-repository-tests | mutable or frozen: review required | Draft | tests/Cis.Modules.Repository.Tests/RepositoryInitializerTests.cs |
| OwnedEntity | * | entity | unknown | unknown | unknown | cis-modules-repository-tests | mutable or frozen: review required | Draft | tests/Cis.Modules.Repository.Tests/ReferencePreparationTests.cs |
| AgentImportRecord | * | record | unknown (review required) | unknown (review required) | unknown (review required) | cis-modules-agent | persisted import-result JSON; retention policy requires review | Draft | src/Cis.Modules.Agent/AgentService.cs:32; src/Cis.Modules.Agent/AgentService.cs:1052 |
| AgentPermissionRecord | * | record | unknown (review required) | unknown (review required) | unknown (review required) | cis-modules-agent | per-run permissions JSONL; retention policy requires review | Draft | src/Cis.Modules.Agent/AgentService.cs:52; src/Cis.Modules.Agent/AgentService.cs:2541 |
| AiUsageRecord | * | record | unknown (review required) | unknown (review required) | unknown (review required) | cis-modules-ai | local usage JSONL; retention policy requires review | Draft | src/Cis.Modules.Ai/AiGovernanceService.cs:10; src/Cis.Modules.Ai/AiGovernanceService.cs:100 |
| DecisionRecord | * | record | unknown (review required) | unknown (review required) | unknown (review required) | cis-modules-decision | change decisions Markdown table; lifecycle policy requires review | Draft | src/Cis.Modules.Decision/DecisionContracts.cs:13; src/Cis.Modules.Decision/DecisionService.cs:321 |
| DesignReuseRecord | * | record | unknown (review required) | unknown (review required) | unknown (review required) | cis-modules-design | design reuse Markdown table; lifecycle policy requires review | Draft | src/Cis.Modules.Design/DesignContracts.cs:58; src/Cis.Modules.Design/DesignService.cs:898 |
| IntakeRecord | * | record | unknown (review required) | unknown (review required) | unknown (review required) | cis-modules-brd | embedded feature-intake JSON; lifecycle policy requires review | Draft | src/Cis.Modules.Brd/FeatureIntakeService.cs:261; src/Cis.Modules.Brd/FeatureIntakeService.cs:242 |
| StoryPlanRecord | * | record | unknown (review required) | unknown (review required) | unknown (review required) | cis-modules-brd | story-task JSON; lifecycle policy requires review | Draft | src/Cis.Modules.Brd/FeatureIntakeService.StoryWorkflow.cs:16; src/Cis.Modules.Brd/FeatureIntakeService.StoryWorkflow.cs:22 |

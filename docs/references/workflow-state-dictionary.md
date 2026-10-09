---
title: "Workflow State Dictionary"
type: reference
status: Draft
owner: Repository maintainer
review_cadence: on change
cis:
  stable_id: change-impact-studio:reference:workflow-state-dictionary
---

# Workflow State Dictionary

Governed by `change-impact-studio:spec:workflow-state-dictionary`. CIS seeded 14 row(s) from deterministic repository evidence. Review them before changing this document to Active.

| Workflow ID | Workflow | State | Meaning | Allowed transitions | Triggering commands | Blocking rules | Terminal | Visibility / search behavior | Status |
|---|---|---|---|---|---|---|---|---|---|
| WF-CIS-ABSTRACTIONS-ENGINEERING-GATE-STATE | EngineeringGateState | Failed | Declared state; evidence: src/Cis.Abstractions/CisEngineeringContracts.cs. Transition semantics require review. | unknown | unknown | unknown | unknown | unknown | Draft |
| WF-CIS-ABSTRACTIONS-ENGINEERING-GATE-STATE | EngineeringGateState | Inapplicable | Declared state; evidence: src/Cis.Abstractions/CisEngineeringContracts.cs. Transition semantics require review. | unknown | unknown | unknown | unknown | unknown | Draft |
| WF-CIS-ABSTRACTIONS-ENGINEERING-GATE-STATE | EngineeringGateState | Missing | Declared state; evidence: src/Cis.Abstractions/CisEngineeringContracts.cs. Transition semantics require review. | unknown | unknown | unknown | unknown | unknown | Draft |
| WF-CIS-ABSTRACTIONS-ENGINEERING-GATE-STATE | EngineeringGateState | Passed | Declared state; evidence: src/Cis.Abstractions/CisEngineeringContracts.cs. Transition semantics require review. | unknown | unknown | unknown | unknown | unknown | Draft |
| WF-CIS-ABSTRACTIONS-ENGINEERING-GATE-STATE | EngineeringGateState | Skipped | Declared state; evidence: src/Cis.Abstractions/CisEngineeringContracts.cs. Transition semantics require review. | unknown | unknown | unknown | unknown | unknown | Draft |
| WF-CIS-ABSTRACTIONS-ENGINEERING-GATE-STATE | EngineeringGateState | Stale | Declared state; evidence: src/Cis.Abstractions/CisEngineeringContracts.cs. Transition semantics require review. | unknown | unknown | unknown | unknown | unknown | Draft |
| WF-CIS-MODULES-REPOSITORY-TESTS-DEPOSIT-STATUS | DepositStatus | Matured | Declared state; evidence: tests/Cis.Modules.Repository.Tests/ReferencePreparationTests.cs. Transition semantics require review. | unknown | unknown | unknown | unknown | unknown | Draft |
| WF-CIS-MODULES-REPOSITORY-TESTS-DEPOSIT-STATUS | DepositStatus | Pending | Declared state; evidence: tests/Cis.Modules.Repository.Tests/ReferencePreparationTests.cs. Transition semantics require review. | unknown | unknown | unknown | unknown | unknown | Draft |
| WF-CIS-MODULES-REPOSITORY-TESTS-FAKE-STATUS | FakeStatus | Never | Declared state; evidence: tests/Cis.Modules.Repository.Tests/ReferencePreparationTests.cs. Transition semantics require review. | unknown | unknown | unknown | unknown | unknown | Draft |
| WF-CIS-MODULES-REPOSITORY-TESTS-ORDER-STATUS | OrderStatus | Completed | Declared state; evidence: tests/Cis.Modules.Repository.Tests/RepositoryInitializerTests.cs. Transition semantics require review. | unknown | unknown | unknown | unknown | unknown | Draft |
| WF-CIS-MODULES-REPOSITORY-TESTS-ORDER-STATUS | OrderStatus | Draft | Declared state; evidence: tests/Cis.Modules.Repository.Tests/RepositoryInitializerTests.cs. Transition semantics require review. | unknown | unknown | unknown | unknown | unknown | Draft |
| WF-CIS-MODULES-REPOSITORY-TESTS-ORDER-STATUS | OrderStatus | Submitted | Declared state; evidence: tests/Cis.Modules.Repository.Tests/RepositoryInitializerTests.cs. Transition semantics require review. | unknown | unknown | unknown | unknown | unknown | Draft |
| WF-CIS-MODULES-REPOSITORY-TESTS-VERIFICATION-STATUS | VerificationStatus | Pending | Declared state; evidence: tests/Cis.Modules.Repository.Tests/ReferencePreparationTests.cs. Transition semantics require review. | unknown | unknown | unknown | unknown | unknown | Draft |
| WF-CIS-MODULES-REPOSITORY-TESTS-VERIFICATION-STATUS | VerificationStatus | Verified | Declared state; evidence: tests/Cis.Modules.Repository.Tests/ReferencePreparationTests.cs. Transition semantics require review. | unknown | unknown | unknown | unknown | unknown | Draft |

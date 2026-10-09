---
title: "Command Dictionary"
type: reference
status: Draft
owner: Repository maintainer
review_cadence: on change
cis:
  stable_id: change-impact-studio:reference:command-dictionary
---

# Command Dictionary

Governed by `change-impact-studio:spec:command-dictionary`. CIS seeded rows from deterministic repository evidence; the synthetic example command is identified separately. Review them before changing this document to Active.

| Command ID | Command name | Module | Actor / trigger | API / screen / source | Preconditions | Result | Emits events | Permissions / auth | Status | Evidence |
|---|---|---|---|---|---|---|---|---|---|---|
| CMD-CIS-MODULES-REFERENCES-TESTS-CACHED-COMMAND | CachedCommand | cis-modules-references-tests | unknown | tests/Cis.Modules.References.Tests/ReferenceGovernanceTests.cs | unknown | unknown | unknown | unknown | Draft | tests/Cis.Modules.References.Tests/ReferenceGovernanceTests.cs |
| CMD-CIS-MODULES-REFERENCES-TESTS-CREATE-LIST-COMMAND | CreateListCommand | cis-modules-references-tests | unknown | tests/Cis.Modules.References.Tests/ReferenceGovernanceTests.cs | unknown | unknown | unknown | unknown | Draft | tests/Cis.Modules.References.Tests/ReferenceGovernanceTests.cs |
| CMD-CIS-MODULES-REFERENCES-TESTS-GENERATED-COMMAND | GeneratedCommand | cis-modules-references-tests | unknown | tests/Cis.Modules.References.Tests/ReferenceGovernanceTests.cs | unknown | unknown | unknown | unknown | Draft | tests/Cis.Modules.References.Tests/ReferenceGovernanceTests.cs |
| CMD-CIS-MODULES-REFERENCES-TESTS-REAL-COMMAND | RealCommand | cis-modules-references-tests | unknown | tests/Cis.Modules.References.Tests/ReferenceGovernanceTests.cs | unknown | unknown | unknown | unknown | Draft | tests/Cis.Modules.References.Tests/ReferenceGovernanceTests.cs |
| CMD-CIS-MODULES-REFERENCES-TESTS-TEST-ONLY-COMMAND | TestOnlyCommand | cis-modules-references-tests | unknown | tests/Cis.Modules.References.Tests/ReferenceGovernanceTests.cs | unknown | unknown | unknown | unknown | Draft | tests/Cis.Modules.References.Tests/ReferenceGovernanceTests.cs |
| CMD-CIS-MODULES-REPOSITORY-REPOSITORY-EXAMPLE-COMMAND | RepositoryExampleCommand | cis-modules-repository | unknown | src/Cis.Modules.Repository/RepositoryExampleCommand.cs | unknown | unknown | unknown | unknown | Draft | src/Cis.Modules.Repository/RepositoryExampleCommand.cs |
| CMD-CIS-MODULES-REPOSITORY-TESTS-CREATE-ORDER-COMMAND | CreateOrderCommand | cis-modules-repository-tests | unknown | tests/Cis.Modules.Repository.Tests/RepositoryInitializerTests.cs | unknown | unknown | unknown | unknown | Draft | tests/Cis.Modules.Repository.Tests/RepositoryInitializerTests.cs |
| CMD-EXAMPLE-CLI-READING-COMMAND | ReadingCommand | example-cli (bundled synthetic example) | CLI caller invokes `apply --input <file>` | examples/dotnet-engineering/src/Example.Cli/ReadingCommand.cs | Valid bounded input and EXAMPLE_DATABASE_CONNECTION for an isolated PostgreSQL database | Idempotent stored readings and versioned JSON summary; nonzero exit on failure | No domain events declared | Database permissions from the supplied connection; not a CIS authorization boundary | Draft | examples/dotnet-engineering/src/Example.Cli/ReadingCommand.cs |

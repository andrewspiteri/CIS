---
title: "API Dictionary"
type: reference
status: Draft
owner: Repository maintainer
review_cadence: on change
cis:
  stable_id: change-impact-studio:reference:api-dictionary
---

# API Dictionary

Governed by `change-impact-studio:spec:api-dictionary`. CIS seeded 7 row(s) from deterministic repository evidence. Review them before changing this document to Active.

| API ID | Version | Method | Path | Module | Host | Exposure | Consumer | Request contract | Response contract | Permission / auth | OpenAPI operation | Trusted scope source | Rate-limit policy | Idempotency / concurrency | Collection semantics | Error contract | Cache policy | Data access path | Status | Evidence | Known tests | Notes |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| cis-modules-api-tests:get:profile | unversioned | GET | /profile | cis-modules-api-tests | cis-modules-api-tests | unclassified | unknown | unknown | unknown | unknown | unknown | unknown | unknown | unknown | unknown | unknown | unknown | unknown | Draft | tests/Cis.Modules.Api.Tests/ApiGovernanceTests.cs | unknown | Deterministically discovered from an MVC controller; governance fields require review. |
| cis-modules-api-tests:post:profile | unversioned | POST | /profile | cis-modules-api-tests | cis-modules-api-tests | unclassified | unknown | unknown | unknown | unknown | unknown | unknown | unknown | unknown | unknown | unknown | unknown | unknown | Draft | tests/Cis.Modules.Api.Tests/ApiGovernanceTests.cs | unknown | Deterministically discovered from an MVC controller; governance fields require review. |
| cis-modules-repository:get:root | unversioned | GET | ,  | cis-modules-repository | cis-modules-repository | unclassified | unknown | unknown | unknown | unknown | unknown | unknown | unknown | unknown | unknown | unknown | unknown | unknown | Draft | src/Cis.Modules.Repository/RepositoryClassifier.cs | unknown | Deterministically discovered; governance fields require review. |
| cis-modules-repository:post:var-ispackage-containsany-projecttext | unversioned | POST | );         var isPackage = ContainsAny(projectText,  | cis-modules-repository | cis-modules-repository | unclassified | unknown | unknown | unknown | unknown | unknown | unknown | unknown | unknown | unknown | unknown | unknown | unknown | Draft | src/Cis.Modules.Repository/RepositoryClassifier.cs | unknown | Deterministically discovered; governance fields require review. |
| example-web:get:api-readings | unversioned | GET | /api/readings | example-web | example-web | unclassified | unknown | unknown | unknown | unknown | unknown | unknown | unknown | unknown | unknown | unknown | unknown | unknown | Draft | examples/dotnet-engineering/src/Example.Web/ReadingEndpoints.cs | unknown | Deterministically discovered; governance fields require review. |
| example-web:get:root | unversioned | GET | / | example-web | example-web | unclassified | unknown | unknown | unknown | unknown | unknown | unknown | unknown | unknown | unknown | unknown | unknown | unknown | Draft | examples/dotnet-engineering/src/Example.Web/ReadingEndpoints.cs | unknown | Deterministically discovered; governance fields require review. |
| example-web:post:api-readings | unversioned | POST | /api/readings | example-web | example-web | unclassified | unknown | unknown | unknown | unknown | unknown | unknown | unknown | unknown | unknown | unknown | unknown | unknown | Draft | examples/dotnet-engineering/src/Example.Web/ReadingEndpoints.cs | unknown | Deterministically discovered; governance fields require review. |

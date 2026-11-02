---
title: "Permissions Dictionary"
type: reference
status: Draft
owner: Repository maintainer
review_cadence: on change
cis:
  stable_id: change-impact-studio:reference:permissions-dictionary
---

# Permissions Dictionary

Governed by `change-impact-studio:spec:permissions-dictionary`. The Draft rows below record authorization policies in the tracked synthetic Example.Web sample. Role mappings, customer assignment, and frontend capability mappings require review.

| Permission code | Permission name | Domain | Capability group | Action | Scope type | Applies to | Default role mappings | Customer assignable | Endpoint / API usage | Frontend capability | Source location | Status | Notes |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| read | Read sample readings | Example.Web synthetic sample | sample readings API | read | `scope` claim | authenticated synthetic sample identities | not specified by the sample policy | not specified | GET `/api/readings`; configured OpenAPI endpoint | not specified | examples/dotnet-engineering/src/Example.Web/ReadingAuthentication.cs:30; examples/dotnet-engineering/src/Example.Web/ReadingEndpoints.cs:18; examples/dotnet-engineering/src/Example.Web/ReadingWebHost.cs:27 | Draft | Requires an authenticated user and `scope=readings.read`. Applies only to the tracked synthetic Example.Web sample. |
| write | Write sample readings | Example.Web synthetic sample | sample readings API | write | `scope` claim | authenticated synthetic sample identities | not specified by the sample policy | not specified | POST `/api/readings` | not specified | examples/dotnet-engineering/src/Example.Web/ReadingAuthentication.cs:31; examples/dotnet-engineering/src/Example.Web/ReadingEndpoints.cs:19 | Draft | Requires an authenticated user and `scope=readings.write`. Applies only to the tracked synthetic Example.Web sample. |

---
applyTo: "examples/dotnet-engineering/tests/migration/**/*.cs"
---

# C# and .NET guidance for example-migrationtests

Component root: `examples/dotnet-engineering/tests/migration`.

Run the narrowest relevant dotnet build and test commands. Preserve nullable analysis and treat warnings as defects.

Before changing behavior, inspect the applicable specifications, references, callers, and tests.

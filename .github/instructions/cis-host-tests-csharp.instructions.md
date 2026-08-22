---
applyTo: "tests/Cis.Host.Tests/**/*.cs"
---

# C# and .NET guidance for cis-host-tests

Component root: `tests/Cis.Host.Tests`.

Run the narrowest relevant dotnet build and test commands. Preserve nullable analysis and treat warnings as defects.

Before changing behavior, inspect the applicable specifications, references, callers, and tests.

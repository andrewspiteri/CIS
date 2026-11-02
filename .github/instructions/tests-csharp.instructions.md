---
applyTo: "tests/fixtures/MSTestAdapterCompat/Tests/**/*.cs"
---

# C# and .NET guidance for tests

Component root: `tests/fixtures/MSTestAdapterCompat/Tests`.

Run the narrowest relevant dotnet build and test commands. Preserve nullable analysis and treat warnings as defects.

Before changing behavior, inspect the applicable specifications, references, callers, and tests.

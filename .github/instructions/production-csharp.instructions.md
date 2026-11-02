---
applyTo: "tests/fixtures/MSTestAdapterCompat/Production/**/*.cs"
---

# C# and .NET guidance for production

Component root: `tests/fixtures/MSTestAdapterCompat/Production`.

Run the narrowest relevant dotnet build and test commands. Preserve nullable analysis and treat warnings as defects.

Before changing behavior, inspect the applicable specifications, references, callers, and tests.

---
applyTo: "tools/Cis.IncrementalValidation.Tool/**/*.cs"
---

# C# and .NET guidance for cis-incrementalvalidation-tool

Component root: `tools/Cis.IncrementalValidation.Tool`.

Run the narrowest relevant dotnet build and test commands. Preserve nullable analysis and treat warnings as defects.

Before changing behavior, inspect the applicable specifications, references, callers, and tests.

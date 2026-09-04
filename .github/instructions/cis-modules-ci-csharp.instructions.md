---
applyTo: "src/Cis.Modules.Ci/**/*.cs"
---

# C# and .NET guidance for cis-modules-ci

Component root: `src/Cis.Modules.Ci`.

Run the narrowest relevant dotnet build and test commands. Preserve nullable analysis and treat warnings as defects.

Before changing behavior, inspect the applicable specifications, references, callers, and tests.

---
applyTo: "tools/Cis.Testing.NativeFixture/**/*.cs"
---

# C# and .NET guidance for cis-testing-nativefixture

Component root: `tools/Cis.Testing.NativeFixture`.

Run the narrowest relevant dotnet build and test commands. Preserve nullable analysis and treat warnings as defects.

Before changing behavior, inspect the applicable specifications, references, callers, and tests.

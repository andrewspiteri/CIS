---
applyTo: "tools/Cis.Testing.NativeFixture/**/*.cs"
---

# C# and .NET guidance for cis-testing-nativefixture

Component root: `tools/Cis.Testing.NativeFixture`.

Run the narrowest relevant dotnet build and test commands. Preserve nullable analysis and treat warnings as defects.

Before changing behavior, inspect the applicable specifications, references, callers, and tests.

This fixture emits synthetic native protocol results for focused fault tests. It
cannot prove real mutant activation, native assertion strength or representative
runner readiness. Use the maintained real MSTest/Stryker fixture for those claims;
keep both routes bounded and preserve truthful evidence coverage.

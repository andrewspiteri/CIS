using System.Reflection;
using Example.Core;
using Example.Persistence;
using Xunit.Sdk;

namespace Example.UnitTests;

public sealed class ArchitectureTests
{
    [Fact]
    [Trait("Layer", "architecture")]
    public void CoreHasNoAdapterOrDatabaseDependencies()
        => AssertCoreBoundary(typeof(Reading).Assembly);

    [Fact]
    [Trait("Layer", "architecture")]
    public void DatabaseAdapterDemonstratesThatTheBoundaryRejectsViolations()
        => Assert.ThrowsAny<XunitException>(() => AssertCoreBoundary(typeof(PostgresReadingStore).Assembly));

    private static void AssertCoreBoundary(Assembly assembly)
    {
        Assert.NotEmpty(assembly.GetExportedTypes());
        var dependencies = assembly.GetReferencedAssemblies();
        Assert.NotEmpty(dependencies);
        Assert.DoesNotContain(dependencies, dependency => dependency.Name is
            "Npgsql" or "Example.Persistence" or "Example.Cli" or "System.CommandLine");
    }
}

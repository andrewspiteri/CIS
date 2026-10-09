namespace Cis.Modules.Repository.Tests;

public sealed class RepositoryExampleTests
{
    [Fact]
    public void ExportPreservesExistingDirectoriesAndSupportsReadOnlyPreview()
    {
        var parent = Path.Combine(Path.GetTempPath(), "cis-example-tests");
        var root = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        var distribution = Path.Combine(root, "distribution");
        var source = Path.Combine(distribution, "examples/dotnet-engineering");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "README.md"), "Native recipe fixture");
        try
        {
            var service = new RepositoryExample(distribution);
            Assert.Equal("available", service.Export(root, null, false).Status);
            Assert.Equal("planned", service.Export(root, "reference", true).Status);
            Assert.False(Directory.Exists(Path.Combine(root, "reference")));
            Assert.Equal("exported", service.Export(root, "reference", false).Status);
            File.WriteAllText(Path.Combine(root, "reference/README.md"), "User customization");
            Assert.Equal(2, service.Export(root, "reference", false).ExitCode);
            Assert.Equal("User customization", File.ReadAllText(Path.Combine(root, "reference/README.md")));
            Assert.Equal(2, service.Export(root, "../outside", false).ExitCode);
        }
        finally
        {
            if (!Cis.Abstractions.CisPathSafety.IsUnderRoot(parent, root)) throw new InvalidOperationException();
            Directory.Delete(root, true);
        }
    }
}

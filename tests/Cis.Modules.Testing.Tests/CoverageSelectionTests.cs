using System.Text.Json;
using Cis.Modules.Repository;
using Cis.Modules.Workflow;
using Xunit;

namespace Cis.Modules.Testing.Tests;

public sealed partial class TestingServiceTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public void NativeCollectorSelectionRequiresExactlyOneScopedReport(int reports, bool valid)
    {
        using var repository = TestRepository.Create();
        repository.Write("docs/cis/references/test-suite-profile.md", Profile("unit", "junit", ".cis/local/results/unit.xml")
            .Replace("| - | - | - | always", "| .cis/local/results/unit/*/coverage.cobertura.xml | - | - | always", StringComparison.Ordinal));
        repository.Write("docs/cis/workflows/verify.md", "| Step | Command | Depends on | Continue on failure | Timeout seconds | Test suites |\n| unit | dotnet --version | - | no | 30 | api-unit |\n");
        var definition = new WorkflowService(new CisRepositoryContextResolver()).Describe(repository.Path, "verify").Workflow!;
        // Protocol fixture; execution of a real collector is qualified separately.
        repository.Write(".cis/local/workflows/run/state.json", JsonSerializer.Serialize(new WorkflowRunState(2, "run", "verify", definition.Digest,
            "succeeded", "2026-10-05T00:00:00Z", "2026-10-05T00:01:00Z",
            [new("unit", "succeeded", 0, "2026-10-05T00:00:00Z", "2026-10-05T00:01:00Z", 1, "unit.log", null)]), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        repository.Write(".cis/local/results/unit.xml", "<testsuite><testcase name=\"native case\" /></testsuite>");
        for (var index = 0; index < reports; index++)
            repository.Write($".cis/local/results/unit/{index}/coverage.cobertura.xml", "<coverage line-rate=\"1\" branch-rate=\"1\" />");
        repository.Write(".cis/local/results/unit/deployment/In/host/coverage.cobertura.xml", "<coverage line-rate=\"1\" branch-rate=\"1\" />");
        Assert.Equal(0, Service().Validate(repository.Path, true).ExitCode);
        var result = Service().Reconcile(repository.Path, "run");
        Assert.Equal(valid, result.ExitCode == 0);
        if (valid) Assert.Equal(100, Assert.Single(result.Manifest!.Suites).Coverage!.Lines);
    }
}

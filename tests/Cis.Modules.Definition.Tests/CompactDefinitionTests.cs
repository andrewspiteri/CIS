using System.Text.Json;
using Xunit;

namespace Cis.Modules.Definition.Tests;

public sealed partial class DefinitionWizardTests
{
    [Fact]
    public void SummaryPreservesReadinessWhileOmittingDetailedQuestions()
    {
        using var repository = TemporaryRepository.Create();
        using var application = CreateApplication();
        Assert.Equal(0, Invoke(application, ["workspace", "init", "--repo", repository.Path,
            "--root", "docs/cis", "--ecosystem", "sample", "--product", "sample", "--yes", "--format", "json"]).ExitCode);
        var full = Invoke(application, ["definition", "status", "--workspace", repository.Path, "--format", "json"]);
        var summary = Invoke(application, ["definition", "status", "--workspace", repository.Path, "--summary", "--format", "json"]);
        Assert.Equal(full.ExitCode, summary.ExitCode);
        using var a = JsonDocument.Parse(full.Output);
        using var b = JsonDocument.Parse(summary.Output);
        Assert.Equal(a.RootElement.GetProperty("status").GetString(), b.RootElement.GetProperty("status").GetString());
        Assert.Equal(a.RootElement.GetProperty("readyToActivate").GetBoolean(), b.RootElement.GetProperty("readyToActivate").GetBoolean());
        Assert.True(b.RootElement.GetProperty("detailsOmitted").GetBoolean());
        Assert.False(b.RootElement.TryGetProperty("technicalQuestions", out _));
        Assert.True(summary.Output.Length < full.Output.Length);
    }
}

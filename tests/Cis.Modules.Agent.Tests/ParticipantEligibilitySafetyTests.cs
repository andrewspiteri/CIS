using System.Diagnostics;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentExecutionTests
{
    [Theory]
    [InlineData("locked-feature")]
    [InlineData("oversized-feature")]
    [InlineData("linked-feature")]
    [InlineData("locked-plan")]
    public void ParticipantRun_BlocksUnsafeEligibilityEvidenceBeforeProviderExecution(string scenario)
    {
        using var authority = AgentRepository.Create();
        using var participant = AgentRepository.Create();
        ConfigureParticipantTask(authority, participant);
        const string feature = "docs/cis/specs/features/readings/feature-specification.md";
        var lockedPath = Path.Combine(authority.Path, scenario == "locked-plan"
            ? "docs/cis/changes/CIS-0001/plan.md" : feature);
        if (scenario == "oversized-feature") authority.Write(feature, new string('x', 262145));
        if (scenario == "linked-feature")
        {
            var directory = Path.GetDirectoryName(lockedPath)!;
            var external = Path.Combine(participant.Path, ".cis/local/linked-feature");
            Directory.CreateDirectory(Path.GetDirectoryName(external)!);
            Directory.Move(directory, external);
            if (OperatingSystem.IsWindows())
            {
                var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var argument in new[] { "/d", "/c", "mklink", "/J", Path.GetFullPath(directory), Path.GetFullPath(external) })
                    start.ArgumentList.Add(argument);
                using var process = Process.Start(start)!;
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                Assert.True(process.ExitCode == 0, error);
            }
            else Directory.CreateSymbolicLink(directory, external);
            lockedPath = Path.Combine(external, "feature-specification.md");
        }
        using var locked = scenario == "oversized-feature" ? null
            : File.Open(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var provider = new FakeProvider();

        var result = ParticipantReview(ParticipantService(provider), authority);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal(0, provider.ExecuteCalls);
        Assert.Contains(result.Diagnostics, message => message.Contains("eligibility could not be verified", StringComparison.Ordinal));
        if (scenario == "linked-feature") Assert.Contains(result.Diagnostics, message => message.Contains("unsafe authority artifact", StringComparison.Ordinal));
        if (scenario == "oversized-feature") Assert.Contains(result.Diagnostics, message => message.Contains("bounded size", StringComparison.Ordinal));
    }
}

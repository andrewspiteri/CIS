using System.Diagnostics;
using Cis.Abstractions;

namespace Cis.Modules.Delivery.Tests;

public sealed partial class EngineeringCompletionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OutputNamesDoNotHideSourceAndTrackedFiles(bool git)
    {
        using var fixture = new Fixture();
        if (git) Git("init");
        Directory.CreateDirectory(Path.Combine(fixture.Root, "src/Artifacts"));
        File.WriteAllText(Path.Combine(fixture.Root, "src/Artifacts/Contract.cs"), "class Contract;");
        var context = new CisRepositoryContext(fixture.Root, "fixture", "docs", Path.Combine(fixture.Root, "docs"), Path.Combine(fixture.Root, "docs/catalog.yml"));
        var before = CisExecutionIdentity.Capture(context);
        File.AppendAllText(Path.Combine(fixture.Root, "src/Artifacts/Contract.cs"), "// changed");
        Assert.NotEqual(before, CisExecutionIdentity.Capture(context));
        if (!git) return;
        File.WriteAllText(Path.Combine(fixture.Root, ".gitignore"), "bin/\n");
        Directory.CreateDirectory(Path.Combine(fixture.Root, "bin"));
        var baseline = CisExecutionIdentity.Capture(context);
        File.WriteAllText(Path.Combine(fixture.Root, "bin/generated.txt"), "output");
        Assert.Equal(baseline, CisExecutionIdentity.Capture(context));
        Git("add", "-f", "bin/generated.txt");
        var tracked = CisExecutionIdentity.Capture(context);
        Assert.NotEqual(baseline, tracked);
        File.AppendAllText(Path.Combine(fixture.Root, "bin/generated.txt"), "changed");
        Assert.NotEqual(tracked, CisExecutionIdentity.Capture(context));

        void Git(params string[] arguments)
        {
            var start = new ProcessStartInfo("git") { WorkingDirectory = fixture.Root };
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add("safe.directory=" + fixture.Root);
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            Assert.Equal(0, CisProcessSafety.Run(start, TimeSpan.FromSeconds(20)).ExitCode);
        }
    }
}

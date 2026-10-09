using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Agent;
using Cis.Modules.Repository;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentExecutionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ParticipantRun_DetectsCanonicalSelectionChangesIncludingInitiallyAbsentSelection(bool initiallySelected)
    {
        using var authority = AgentRepository.Create();
        using var participant = AgentRepository.Create();
        ConfigureParticipantTask(authority, participant);
        if (initiallySelected) authority.Write(CisProductDocumentPaths.SelectionFile, """{"business":"docs/cis/specs/business-requirements.md"}""");
        authority.Write("docs/cis/specs/alternate-business.md", "Different canonical authority.");
        var provider = new FakeProvider(duringExecution: _ => authority.Write(CisProductDocumentPaths.SelectionFile,
            """{"business":"docs/cis/specs/alternate-business.md"}"""));
        var service = ParticipantService(provider);

        var run = ParticipantReview(service, authority);

        Assert.Equal(CisAgentRunStates.InvalidEvidence, run.Run!.Manifest.Status);
        Assert.Equal("Approved product requirements.", authority.Read("docs/cis/specs/business-requirements.md"));
        Assert.NotEqual(0, service.Resume(authority.Path, run.Run.Manifest.RunId, "Continue", "Agent", "Retry", false,
            TestContext.Current.CancellationToken).ExitCode);
        Assert.Equal(1, provider.ExecuteCalls);
    }

    [Theory]
    [InlineData("artifact-limit")]
    [InlineData("aggregate-limit")]
    [InlineData("aggregate-overflow")]
    [InlineData("invalid-utf8")]
    [InlineData("locked-file")]
    public void ParticipantRun_EnforcesByteBoundariesAndReturnsStructuredReadFailures(string scenario)
    {
        using var authority = AgentRepository.Create();
        using var participant = AgentRepository.Create();
        ConfigureParticipantTask(authority, participant);
        var provider = new FakeProvider();
        var service = ParticipantService(provider);
        const string business = "docs/cis/specs/business-requirements.md";
        if (scenario == "artifact-limit") authority.Write(business, new string('x', 262144));
        if (scenario.StartsWith("aggregate", StringComparison.Ordinal))
        {
            var padding = new[] { business, "docs/cis/specs/technical-intent-spec.md", "docs/cis/architecture/overall-solution-design.md",
                "docs/cis/design/ui-direction.md", "docs/cis/plans/high-level-backlog.md" };
            foreach (var path in padding) authority.Write(path, "");
            var prepared = service.Prepare(authority.Path, "CIS-0001", "WORK-090", "fake");
            Assert.NotNull(prepared.Envelope);
            var existing = prepared.Envelope.ContextArtifacts.Sum(path => new FileInfo(Path.Combine(authority.Path, path)).Length);
            var remaining = 1048576 - existing + (scenario == "aggregate-overflow" ? 1 : 0);
            foreach (var path in padding)
            {
                var size = (int)Math.Min(remaining, 262144);
                authority.Write(path, new string('x', size));
                remaining -= size;
            }
            Assert.Equal(0, remaining);
        }
        if (scenario == "invalid-utf8") File.WriteAllBytes(Path.Combine(authority.Path, business), [0xFF, 0xFE, 0xFF]);
        using var locked = scenario == "locked-file" ? File.Open(Path.Combine(authority.Path, business), FileMode.Open, FileAccess.ReadWrite, FileShare.None) : null;

        var run = ParticipantReview(service, authority);

        if (scenario is "aggregate-overflow" or "invalid-utf8" or "locked-file")
        {
            Assert.NotEqual(0, run.ExitCode);
            Assert.Equal(0, provider.ExecuteCalls);
            Assert.NotEmpty(run.Diagnostics);
            return;
        }
        Assert.True(run.ExitCode == 0, string.Join("\n", run.Diagnostics));
        var bound = run.Envelope!.AuthorityArtifacts!;
        Assert.Equal(scenario == "aggregate-limit" ? 1048576 : 262144,
            scenario == "aggregate-limit" ? bound.Sum(item => Encoding.UTF8.GetByteCount(item.Content))
                : Encoding.UTF8.GetByteCount(bound.Single(item => item.Path == business).Content));
        Assert.Equal(0, service.Resume(authority.Path, run.Run!.Manifest.RunId, "Continue unchanged", "Agent", "Resume unchanged context",
            false, TestContext.Current.CancellationToken).ExitCode);
        foreach (var item in bound) Assert.Contains(JsonSerializer.Serialize(item.Content), provider.LastRequest!.Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void ParticipantRun_RejectsLinkedDefaultDocumentBeforeProviderExecution()
    {
        using var authority = AgentRepository.Create();
        using var participant = AgentRepository.Create();
        ConfigureParticipantTask(authority, participant);
        var path = Path.Combine(authority.Path, "docs/cis/specs");
        var external = Path.Combine(participant.Path, ".cis/local/linked-specs");
        Directory.CreateDirectory(Path.GetDirectoryName(external)!);
        Directory.Move(path, external);
        // Keep feature eligibility independent of the linked default product documents.
        authority.Write("docs/cis/features/readings/feature-specification.md", "Preserve the reading identity and UTC event time.");
        const string plan = "docs/cis/changes/CIS-0001/plan.md";
        authority.Write(plan, authority.Read(plan).Replace("docs/cis/specs/features/", "docs/cis/features/", StringComparison.Ordinal));
        if (OperatingSystem.IsWindows())
        {
            // Directory junctions exercise reparse protection without requiring symlink privilege.
            var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "/d", "/c", "mklink", "/J", Path.GetFullPath(path), Path.GetFullPath(external) }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, error);
        }
        else Directory.CreateSymbolicLink(path, external);
        using var locked = File.Open(Path.Combine(external, "business-requirements.md"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var provider = new FakeProvider();

        var run = ParticipantReview(ParticipantService(provider), authority);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Equal(0, provider.ExecuteCalls);
        Assert.Contains(run.Diagnostics, message => message.Contains("unsafe authority artifact", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeStdinTransport_PreservesLargeInitialAndResumedPromptBytes(bool resumed)
    {
        var payload = JsonSerializer.Serialize(new { context = new string('x', 1048576), continuation = resumed ? "Continue unchanged" : "Start" });
        ProcessStartInfo start;
        if (OperatingSystem.IsWindows())
        {
            start = new("powershell.exe");
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command",
                "[Console]::InputEncoding=[Text.Encoding]::UTF8; $value=[Console]::In.ReadToEnd(); $hash=[Security.Cryptography.SHA256]::Create().ComputeHash([Text.Encoding]::UTF8.GetBytes($value)); [Console]::WriteLine([BitConverter]::ToString($hash).Replace('-','').ToLowerInvariant())" }) start.ArgumentList.Add(argument);
        }
        else start = new("sha256sum");
        var output = new List<string>();

        var result = CisAgentProcessRunner.RunLines(start, payload, TimeSpan.FromSeconds(30), output.Add, null,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.StartsWith(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload))), Assert.Single(output), StringComparison.Ordinal);
    }

    private static AgentService ParticipantService(FakeProvider provider) => new(new CisRepositoryContextResolver(), [provider],
        new WorkspaceRegistry(new CisRepositoryContextResolver()), clock: Clock);

    [Fact]
    public void ParticipantPreparation_ReportsInvalidDocumentSelectionAsStructuredFailure()
    {
        using var authority = AgentRepository.Create();
        using var participant = AgentRepository.Create();
        ConfigureParticipantTask(authority, participant);
        authority.Write(CisProductDocumentPaths.SelectionFile, "invalid json");
        var result = ParticipantService(new FakeProvider()).Prepare(authority.Path, "CIS-0001", "WORK-090", "fake");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(result.Diagnostics, message => message.Contains("context could not be prepared", StringComparison.Ordinal));
    }
    private static AgentResult ParticipantReview(AgentService service, AgentRepository authority) => service.Run(authority.Path,
        "CIS-0001", "WORK-090", "fake", CisAgentRunModes.Review, CisAgentPermissions.ReadOnly, "participant", "fake-json", 60,
        false, "Agent", TestContext.Current.CancellationToken);
}

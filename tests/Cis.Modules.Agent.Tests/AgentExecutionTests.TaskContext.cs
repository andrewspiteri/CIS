using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cis.Abstractions;
using Cis.Host;
using Cis.Modules.Agent;
using Cis.Modules.Repository;
using Microsoft.Extensions.DependencyInjection;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentExecutionTests
{
    [Fact]
    public void RoutineSummary_BoundsManyLargeUnicodeBlockersAndDisclosesOmissions()
    {
        string message = "ERROR: " + string.Concat(Enumerable.Repeat("日本語 😀 ", 2_000));
        var envelope = new AgentTaskEnvelope(1, message, "fixture", message, message, "portable", message,
            "digest", "2026-11-24T00:00:00Z", "Canonical task", [], [])
        {
            TaskContext = new(1, message, message, null, message, message, 16_000, 64_000, 0, 0, 0, 1, [])
        };
        var result = new AgentResult("blocked", null, envelope, [], [], [], [], null,
            Enumerable.Repeat(message, 200).ToArray(), false);

        string output = AgentRoutineSummary.Render(result, "json");

        using var json = JsonDocument.Parse(output);
        Assert.Equal(200, json.RootElement.GetProperty("blockerCount").GetInt32());
        Assert.True(json.RootElement.GetProperty("diagnostics").GetArrayLength() > 0);
        Assert.True(json.RootElement.GetProperty("omittedDiagnostics").GetInt32() > 0);
        Assert.True(json.RootElement.GetProperty("diagnosticTextTruncated").GetBoolean());
        Assert.True(json.RootElement.GetProperty("metadataTextTruncated").GetBoolean());
        Assert.Equal(result.ExitCode, json.RootElement.GetProperty("exitCode").GetInt32());
        Assert.True(output.Length + Environment.NewLine.Length <= 12_000);
        Assert.True(Encoding.UTF8.GetByteCount(output + Environment.NewLine) <= 48_000);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Prepare_RefusesOversizedProviderDeliveryDespiteItsReportedByteCount(bool oversizedMetadata)
    {
        using var repository = AgentRepository.Create(git: false);
        repository.AddEligibleChange();
        var service = new AgentService(new TaskContextPreparer(oversized: true, oversizedMetadata: oversizedMetadata),
            new CisRepositoryContextResolver(), [], clock: Clock);

        var result = service.Prepare(repository.Path, "CIS-0001", "WORK-090", "portable");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("unavailable", result.Envelope!.TaskContext!.Status);
        Assert.Contains("CIS-TASK-CONTEXT-PROVIDER-INVALID", result.Envelope.TaskContext.DiagnosticCodes);
        Assert.True(result.Envelope.TaskContext.DeliveredCharacters > 0);
        Assert.True(result.Envelope.TaskContext.DeliveredCharacters <= 16_000);
        Assert.True(result.Envelope.TaskContext.DeliveredUtf8Bytes <= 64_000);
        Assert.DoesNotContain(new string('x', 1_000), result.Envelope.TaskContext.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Prepare_AddsAdvisoryContextWithoutChangingCanonicalTaskAuthority()
    {
        using var repository = AgentRepository.Create(git: false);
        repository.AddEligibleChange();
        var preparer = new TaskContextPreparer();
        var service = new AgentService(preparer, new CisRepositoryContextResolver(), [], clock: Clock);
        const string path = "docs/cis/changes/CIS-0001/agent-tasks/WORK-090.md";
        repository.Write(path, repository.Read(path).Replace("targets:", "context_roots: [\"fixture:task:original\"]\ntargets:", StringComparison.Ordinal));
        string canonical = repository.Read(path);

        var result = service.Prepare(repository.Path, "CIS-0001", "WORK-090", "portable");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(path, preparer.Request!.CanonicalTaskPath);
        Assert.Equal(["fixture:task:original"], preparer.Request.ContextRoots);
        Assert.Equal(canonical, result.Envelope!.InstructionMarkdown);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))), result.Envelope.CanonicalTaskDigest);
        Assert.Equal("Fixture implementation and connected checks; their results are unknown.", result.Envelope.TaskContext!.Content);
        string advisoryJson = JsonSerializer.Serialize(result.Envelope.TaskContext, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        string resultJson = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        using var actualResult = JsonDocument.Parse(resultJson);
        string actualValue = actualResult.RootElement.GetProperty("envelope").GetProperty("taskContext").GetRawText();
        string promptFrame = AgentService.RenderTaskContextPrompt(result.Envelope.TaskContext);
        Assert.Equal(new[] { advisoryJson.Length, actualValue.Length, promptFrame.Length }.Max(), result.Envelope.TaskContext.DeliveredCharacters);
        Assert.Equal(new[] { advisoryJson, actualValue, promptFrame }.Max(value => Encoding.UTF8.GetByteCount(value)), result.Envelope.TaskContext.DeliveredUtf8Bytes);
        Assert.True(result.Envelope.TaskContext.DeliveredCharacters <= 16_000);
        Assert.True(result.Envelope.TaskContext.DeliveredUtf8Bytes <= 64_000);
        Assert.DoesNotContain("derived-context.json", result.Envelope.ContextArtifacts);
        Assert.DoesNotContain("fixture:task:original", result.Envelope.ContextArtifacts);
        Assert.Equal(canonical, repository.Read(path));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[\"../other\"]")]
    [InlineData("[\"one\",\"two\",\"three\",\"four\",\"five\"]")]
    [InlineData("[")]
    public void Prepare_InvalidAdvisoryRootMetadataCannotChangeAuthorityOrReachTheProvider(string metadata)
    {
        using var repository = AgentRepository.Create(git: false);
        repository.AddEligibleChange();
        const string path = "docs/cis/changes/CIS-0001/agent-tasks/WORK-090.md";
        repository.Write(path, repository.Read(path).Replace("targets:", "context_roots: " + metadata + "\ntargets:", StringComparison.Ordinal));
        string canonical = repository.Read(path);
        var preparer = new TaskContextPreparer();
        var service = new AgentService(preparer, new CisRepositoryContextResolver(), [], clock: Clock);

        var result = service.Prepare(repository.Path, "CIS-0001", "WORK-090", "portable");

        Assert.Equal(0, result.ExitCode);
        Assert.Null(preparer.Request);
        Assert.Equal("unavailable", result.Envelope!.TaskContext!.Status);
        Assert.Contains("CIS-TASK-CONTEXT-ROOTS-INVALID", result.Envelope.TaskContext.DiagnosticCodes);
        Assert.Equal(canonical, result.Envelope.InstructionMarkdown);
        Assert.Equal(canonical, repository.Read(path));
    }

    [Fact]
    public void Run_DeliversDerivedContextAsAdvisoryEvidenceBesideUnchangedTask()
    {
        using var repository = AgentRepository.Create(git: false);
        repository.AddEligibleChange();
        var provider = new FakeProvider();
        var service = new AgentService(new TaskContextPreparer(), new CisRepositoryContextResolver(), [provider], clock: Clock);
        using var observations = CisInvocationTelemetry.Begin("agent run");

        var result = service.Run(repository.Path, "CIS-0001", "WORK-090", "fake", CisAgentRunModes.Review,
            CisAgentPermissions.ReadOnly, null, "fake-json", 60, false, "Synthetic reviewer", TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Derived task context (advisory evidence, not execution authority)", provider.LastRequest!.Prompt, StringComparison.Ordinal);
        Assert.Contains("their results are unknown", provider.LastRequest.Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Fixture implementation", result.Envelope!.InstructionMarkdown, StringComparison.Ordinal);
        Assert.Equal((long?)Encoding.UTF8.GetByteCount(provider.LastRequest.Prompt), observations.Snapshot().DeliveredContextUtf8Bytes);
    }

    [Fact]
    public void PrepareSummary_IsOptInAndKeepsEnvelopeAvailableWithoutDumpingTaskText()
    {
        using var repository = AgentRepository.Create(git: false);
        repository.AddEligibleChange();
        var recorder = new TaskContextUsageRecorder();
        using var application = new CisHostBuilder().AddModule(new RepositoryModule()).AddModule(new AgentModule())
            .AddModule(new TaskContextObservationModule(recorder)).Build();
        string Invoke(bool summary)
        {
            var previous = Console.Out;
            using var output = new StringWriter();
            try
            {
                Console.SetOut(output);
                string[] arguments = ["agent", "prepare", "CIS-0001", "WORK-090", "--repo", repository.Path, "--format", "json"];
                Assert.Equal(0, application.Invoke(summary ? [.. arguments, "--summary"] : arguments));
                return output.ToString();
            }
            finally
            {
                Console.SetOut(previous);
            }
        }
        using var complete = JsonDocument.Parse(Invoke(false));
        string actualContext = complete.RootElement.GetProperty("envelope").GetProperty("taskContext").GetRawText();
        Assert.Equal((long?)Encoding.UTF8.GetByteCount(actualContext), recorder.Last!.Observations!.DeliveredContextUtf8Bytes);
        Assert.True(recorder.Last.Observations.DeliveredContextUtf8Bytes < recorder.Last.StandardOutputUtf8Bytes);
        string compact = Invoke(true);
        Assert.Equal(0L, recorder.Last!.Observations!.DeliveredContextUtf8Bytes);
        using var summary = JsonDocument.Parse(compact);
        Assert.True(complete.RootElement.GetProperty("envelope").TryGetProperty("instructionMarkdown", out _));
        Assert.Equal("routine-summary", summary.RootElement.GetProperty("kind").GetString());
        Assert.False(summary.RootElement.TryGetProperty("envelope", out _));
        Assert.Equal("WORK-090", summary.RootElement.GetProperty("taskId").GetString());
        Assert.True(compact.Length <= 12_000);
        Assert.True(Encoding.UTF8.GetByteCount(compact) <= 48_000);
        Assert.Equal(2, application.Invoke(["agent", "prepare", "CIS-0001", "WORK-090", "--repo", repository.Path,
            "--format", "unsupported", "--summary"]));
    }

    [Fact]
    public void Prepare_RefusesAProviderPayloadThatFitsCompactJsonButExceedsItsDeliveredPrettyFrame()
    {
        using var repository = AgentRepository.Create(git: false);
        repository.AddEligibleChange();
        var preparer = new TaskContextPreparer(fillCompactBudget: true);
        var service = new AgentService(preparer, new CisRepositoryContextResolver(), [], clock: Clock);

        var result = service.Prepare(repository.Path, "CIS-0001", "WORK-090", "portable");

        Assert.True(preparer.ReturnedCompactCharacters < 16_000);
        Assert.True(preparer.ReturnedPrettyCharacters > 16_000);
        Assert.Equal("unavailable", result.Envelope!.TaskContext!.Status);
        Assert.Contains("CIS-TASK-CONTEXT-PROVIDER-INVALID", result.Envelope.TaskContext.DiagnosticCodes);
    }

    private sealed class TaskContextPreparer(bool oversized = false, bool oversizedMetadata = false,
        bool fillCompactBudget = false) : ICisTaskContextPreparer
    {
        public CisTaskContextRequest? Request { get; private set; }
        public int ReturnedCompactCharacters { get; private set; }
        public int ReturnedPrettyCharacters { get; private set; }

        public CisTaskContextDelivery Prepare(CisTaskContextRequest request)
        {
            Request = request;
            var delivery = new CisTaskContextDelivery(1, oversizedMetadata ? new string('x', 50_000) : "prepared", "fresh", "sha256:fixture",
                oversized && !oversizedMetadata ? new string('日', 10_000) : "Fixture implementation and connected checks; their results are unknown.",
                ".cis/local/context/derived-context.json", request.MaxCharacters, request.MaxUtf8Bytes, 600, 600, 150, 1, []);
            if (fillCompactBudget)
            {
                delivery = delivery with { Content = string.Empty };
                int metadataCharacters = JsonSerializer.Serialize(delivery, new JsonSerializerOptions(JsonSerializerDefaults.Web)).Length;
                delivery = delivery with { Content = new string('a', request.MaxCharacters - metadataCharacters - 20) };
            }
            ReturnedCompactCharacters = JsonSerializer.Serialize(delivery, new JsonSerializerOptions(JsonSerializerDefaults.Web)).Length;
            string pretty = JsonSerializer.Serialize(new { envelope = new { taskContext = delivery } },
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
            using var document = JsonDocument.Parse(pretty);
            ReturnedPrettyCharacters = document.RootElement.GetProperty("envelope").GetProperty("taskContext").GetRawText().Length;
            return delivery;
        }
    }

    private sealed class TaskContextUsageRecorder : ICisToolUsageRecorder
    {
        public CisToolUsageCapture? Last { get; private set; }
        public void Record(CisToolUsageCapture capture) => Last = capture;
    }

    private sealed class TaskContextObservationModule(TaskContextUsageRecorder recorder) : ICisModule
    {
        public string Name => "task-context-observation";
        public string Description => "Records actual context delivery in the existing command fixture.";
        public void RegisterServices(IServiceCollection services)
        {
            services.AddSingleton<ICisToolUsageRecorder>(recorder);
            services.AddSingleton<ICisTaskContextPreparer>(new TaskContextPreparer());
        }
        public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services) { }
    }
}

using Cis.Abstractions;
using Cis.Modules.Docs;
using Cis.Modules.Graph;
using Cis.Modules.Repository;
using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace Cis.Modules.Testing.Tests;

public sealed partial class TestingServiceTests
{
    [Fact]
    public void NormalCliContinuationReusesAfterCloseoutCommitAndFreshRealGraphWithoutAnotherNativeLaunch()
    {
        using var fixture = new IncrementalGovernedUnitRetryTests.GovernedUnitFixture();
        using var gitCleanup = new FixtureGitReadOnlyCleanup(fixture.Native.Path);
        fixture.Native.Write(".gitignore", ".cis/local/\n.cis/native-calls.log\n");
        fixture.Native.Write("docs/cis/changes/CIS-0001/verification.md", "---\nstatus: Draft\n---\n# Verification\nNative verification is pending.\n");
        fixture.Native.Write("docs/cis/continuation-notes.md", "---\nstatus: Active\n---\n# Continuation notes\nNative verification is pending.\n");
        var documents = new[]
        {
            ("plan", "docs/cis/changes/CIS-0001/plan.md", "change-plan"), ("impact", "docs/cis/changes/CIS-0001/impact.md", "impact-analysis"),
            ("unit-work", "docs/cis/changes/CIS-0001/agent-tasks/WORK-090.md", "agent-task"),
            ("verification", "docs/cis/changes/CIS-0001/verification.md", "verification"),
            ("continuation-notes", "docs/cis/continuation-notes.md", "verification")
        };
        fixture.Native.Write("docs/cis/catalog.yml", "schema_version: 1\nrepository: sample\ndocuments:\n" + string.Join("", documents.Select(document =>
            $"  - id: {document.Item1}\n    path: {document.Item2}\n    type: {document.Item3}\n    status: Active\n    authority: canonical\n")));
        Git("init", "--quiet");
        Git("add", ".");
        Git("-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid", "-c", "commit.gpgsign=false", "commit", "--quiet", "-m", "Initial native fixture");
        var resolver = new CisRepositoryContextResolver();
        var catalog = new DocumentationCatalogReader();
        var inspector = new MarkdownDocumentInspector();
        var inventory = new DocumentationInventoryService(resolver, catalog, inspector);
        var builder = new GraphBuilder(resolver, catalog, inventory, new DocumentationValidationService(resolver, catalog, inventory, inspector));
        var initialGraph = builder.Build(fixture.Native.Path);
        Assert.True(initialGraph.ExitCode == 0, string.Join("; ", initialGraph.Diagnostics.Select(item => item.Message)));
        var initialSnapshot = new GraphSnapshotReader(resolver).Read(fixture.Native.Path);
        Assert.Equal(0, initialSnapshot.ExitCode);
        foreach (var document in documents) Assert.Contains(initialSnapshot.Manifest!.Inputs, input => input.Path == document.Item2);
        var notesHash = initialSnapshot.Manifest!.Inputs.Single(input => input.Path == "docs/cis/continuation-notes.md").Hash;
        var originalTask = File.ReadAllBytes(fixture.Native.Full("docs/cis/changes/CIS-0001/agent-tasks/WORK-090.md"));
        var originalVerification = File.ReadAllBytes(fixture.Native.Full("docs/cis/changes/CIS-0001/verification.md"));
        var first = Invoke("run");
        Assert.Equal(2, first.GetProperty("executedUnitBatches").GetInt32());
        Assert.True(first.GetProperty("scopeComplete").GetBoolean());
        Assert.All(first.GetProperty("batches").EnumerateArray(), batch => Assert.Equal("all-tests", batch.GetProperty("selection").GetString()));
        var calls = fixture.Native.Calls();
        Assert.Equal(2, calls.Length);
        var accepted = fixture.Native.SavedState();
        var reports = accepted.Batches.Values.Select(batch => fixture.Native.Full(IncrementalTestService.LocalRoot + "/sample/" + batch.Unit!.ReportPath))
            .ToDictionary(path => path, File.ReadAllBytes);
        var priorExecutions = fixture.Work().Executions.ToDictionary(execution => execution.ExecutionId);
        var consumed = fixture.Work().Task!.ConsumedMilliseconds;
        var attempts = fixture.Work().Task!.UsedAttempts;
        var progress = new CisAgentWorkProgressRequest(new("The selected native cases passed.", ["fixture:accepted-native-reports"],
            "Retain the accepted native reports.", "Complete the existing task without changing native inputs.", "Continue with the next task.")
        { CloseAttempt = true, MaterialNewInformation = true }, fixture.Work().Revision);
        fixture.Native.Write(".cis/local/continuation-progress.json", JsonSerializer.Serialize(progress, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var recorded = fixture.Controller.RecordWorkProgress(fixture.Native.Path, "CIS-0001", "WORK-090", ".cis/local/continuation-progress.json", "Controller");
        Assert.True(recorded.ExitCode == 0, string.Join("; ", recorded.Errors));
        fixture.Native.Write("docs/cis/changes/CIS-0001/agent-tasks/WORK-090.md", "---\ntask_status: Complete\ntargets: [\"sample\"]\n---\n# Unit work\nThe existing selected cases passed.\n");
        fixture.Native.Write("docs/cis/changes/CIS-0001/verification.md", "---\nstatus: Complete\n---\n# Verification\nThe selected native reports passed; ordinary continuation reuses them.\n");
        fixture.Native.Write("docs/cis/continuation-notes.md", "---\nstatus: Active\n---\n# Continuation notes\nThe selected native reports passed. Continue with the next task.\n");
        Assert.NotEqual(originalTask, File.ReadAllBytes(fixture.Native.Full("docs/cis/changes/CIS-0001/agent-tasks/WORK-090.md")));
        Assert.NotEqual(originalVerification, File.ReadAllBytes(fixture.Native.Full("docs/cis/changes/CIS-0001/verification.md")));
        Git("add", ".");
        Git("-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid", "-c", "commit.gpgsign=false", "commit", "--quiet", "-m", "Complete native verification");
        var refreshed = builder.Build(fixture.Native.Path, refresh: true);
        Assert.True(refreshed.ExitCode == 0, string.Join("; ", refreshed.Diagnostics.Select(item => item.Message)));
        Assert.NotEqual(initialGraph.BuildId, refreshed.BuildId);
        var refreshedSnapshot = new GraphSnapshotReader(resolver).Read(fixture.Native.Path);
        Assert.Equal(0, refreshedSnapshot.ExitCode);
        Assert.NotEqual(notesHash, refreshedSnapshot.Manifest!.Inputs.Single(input => input.Path == "docs/cis/continuation-notes.md").Hash);
        Assert.Equal(0, builder.Build(fixture.Native.Path).ExitCode);
        var stateBytes = File.ReadAllBytes(fixture.StatePath);

        foreach (var operation in new[] { "plan", "status" })
        {
            var result = Invoke(operation);
            Assert.Equal(0, result.GetProperty("pendingUnitBatches").GetInt32());
            Assert.True(result.GetProperty("scopeComplete").GetBoolean());
        }
        Assert.Equal("verified", Invoke("verify").GetProperty("status").GetString());
        Assert.Equal(stateBytes, File.ReadAllBytes(fixture.StatePath));
        Assert.Equal(consumed, fixture.Work().Task!.ConsumedMilliseconds);
        Assert.Equal(attempts, fixture.Work().Task!.UsedAttempts);
        var reused = Invoke("run");
        Assert.Equal(0, reused.GetProperty("executedUnitBatches").GetInt32());
        Assert.Equal(2, reused.GetProperty("reusedUnitBatches").GetInt32());
        Assert.Equal(calls, fixture.Native.Calls());
        foreach (var report in reports) Assert.Equal(report.Value, File.ReadAllBytes(report.Key));
        foreach (var execution in priorExecutions)
            Assert.Equal(execution.Value, fixture.Work().Executions.Single(item => item.ExecutionId == execution.Key));

        JsonElement Invoke(string operation)
        {
            // Each invocation gets new CLI/services and reads the published graph,
            // as it does in a subsequent ordinary process/session.
            var registry = new MutationCommandRegistry();
            new TestingModule().RegisterCommands(registry, new IncrementalCommandServices(Service(), new GraphSnapshotReader(resolver), fixture.Controller));
            using var output = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
            var previous = Console.Out;
            int exit;
            try
            {
                Console.SetOut(output);
                exit = registry.Root.Parse(["test", "incremental", operation, "--profile", "profile.json", "--repo", fixture.Native.Path,
                    "--layer", "unit", "--format", "json", .. (operation == "verify" ? []
                        : new[] { "--change", "CIS-0001", "--task", "WORK-090", "--authority", fixture.Native.Path, "--actor", "Controller" }),
                    .. (operation == "run" ? new[] { "--max-seconds", "120" } : [])]).Invoke();
            }
            finally { Console.SetOut(previous); }
            Assert.True(exit == 0, operation + ": " + output);
            using var document = JsonDocument.Parse(output.ToString());
            return document.RootElement.Clone();
        }

        void Git(params string[] arguments)
        {
            var start = new ProcessStartInfo("git") { WorkingDirectory = fixture.Native.Path, UseShellExecute = false,
                CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var standard = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            Assert.True(process.WaitForExit(10000), "Fixture Git command exceeded ten seconds.");
            Assert.True(process.ExitCode == 0, standard + error);
        }
    }

    private sealed class FixtureGitReadOnlyCleanup(string repository) : IDisposable
    {
        public void Dispose()
        {
            var owner = Path.GetFullPath(repository);
            var gitRoot = Path.GetFullPath(Path.Combine(owner, ".git"));
            Assert.True(CisPathSafety.IsUnderRoot(owner, gitRoot));
            if (!Directory.Exists(gitRoot)) return;
            Assert.False(CisPathSafety.ContainsReparsePoint(owner, gitRoot));
            Assert.False(CisPathSafety.ContainsReparsePointInTree(gitRoot));
            // Git creates read-only loose objects on Windows. Restore only this
            // test's regular .git files before the existing fixture deletes them.
            foreach (var file in CisPathSafety.EnumerateFiles(gitRoot))
            {
                Assert.True(CisPathSafety.IsUnderRoot(gitRoot, Path.GetFullPath(file)));
                var attributes = File.GetAttributes(file);
                Assert.False(attributes.HasFlag(FileAttributes.ReparsePoint));
                if (attributes.HasFlag(FileAttributes.ReadOnly)) File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
            }
        }
    }

    [Theory]
    [InlineData("refine")]
    [InlineData("revalidate")]
    public void MetadataCommandsAcceptCanonicalAssociationAndHoldBeforeUnaccountedWrites(string operation)
    {
        using var fixture = new IncrementalTestServiceTests.Fixture();
        var registry = new MutationCommandRegistry();
        new TestingModule().RegisterCommands(registry, new IncrementalCommandServices(Service(), fixture));
        var arguments = new List<string> { "test", "incremental", operation, "--profile", "profile.json", "--repo", fixture.Path,
            "--reason", "Reviewed metadata update.", "--change", "CIS-0001", "--task", "WORK-090", "--parent-execution", "native/current-parent",
            "--authority", fixture.Path, "--actor", "Fixture controller", "--format", "json" };
        arguments.AddRange(operation == "refine" ? ["--rerun-profile", "rerun.json"]
            : new[] { "--previous-testing-assembly", typeof(IncrementalTestService).Assembly.Location });
        using var output = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        var previous = Console.Out;
        try
        {
            Console.SetOut(output);
            Assert.Equal(4, registry.Root.Parse(arguments.ToArray()).Invoke());
        }
        finally { Console.SetOut(previous); }
        using var result = System.Text.Json.JsonDocument.Parse(output.ToString());
        Assert.Equal("held", result.RootElement.GetProperty("status").GetString());
        Assert.Equal("missing-work-controller", result.RootElement.GetProperty("workCoverage").GetString());
        Assert.False(Directory.Exists(fixture.Full(IncrementalTestService.LocalRoot)));
        Assert.Empty(fixture.Calls());
    }

    [Fact]
    public void IncrementalCommandsRegisterAndDispatchPlanWithoutExecutingNativeWork()
    {
        using var fixture = new IncrementalTestServiceTests.Fixture();
        var registry = new MutationCommandRegistry();
        new TestingModule().RegisterCommands(registry, new IncrementalCommandServices(Service(), fixture));
        using var output = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        var previous = Console.Out;
        try
        {
            Console.SetOut(output);
            Assert.Equal(0, registry.Root.Parse(["test", "incremental", "plan", "--profile", "profile.json", "--repo", fixture.Path,
                "--layer", "unit", "--max-batches", "1", "--format", "json"]).Invoke());
        }
        finally { Console.SetOut(previous); }
        using var result = System.Text.Json.JsonDocument.Parse(output.ToString());
        Assert.Equal("planned", result.RootElement.GetProperty("status").GetString());
        Assert.Equal(2, result.RootElement.GetProperty("pendingUnitBatches").GetInt32());
        Assert.Empty(fixture.Calls());
    }

    private sealed class IncrementalCommandServices(TestingService service, ICisGraphSnapshotReader graph, object? controller = null) : IServiceProvider
    {
        public object? GetService(Type type) => type == typeof(TestingService) ? service : type == typeof(ICisGraphSnapshotReader) ? graph
            : type == typeof(ICisNativeWorkController) || type == typeof(ICisOwnedStorageController) ? controller : null;
    }

    [Fact]
    public void RevalidateCommandRequiresCompatibilityInputsAndDoesNotExecuteTests()
    {
        using var fixture = new IncrementalTestServiceTests.Fixture();
        var registry = new MutationCommandRegistry();
        new TestingModule().RegisterCommands(registry, new IncrementalCommandServices(Service(), fixture));
        using var output = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        var previous = Console.Out;
        try
        {
            Console.SetOut(output);
            Assert.Equal(0, registry.Root.Parse(["test", "incremental", "revalidate", "--profile", "profile.json", "--repo", fixture.Path,
                "--previous-testing-assembly", typeof(IncrementalTestService).Assembly.Location, "--reason", "Reviewed compatible guard update.",
                "--legacy-session-environment", "--format", "json"]).Invoke());
        }
        finally { Console.SetOut(previous); }
        using var result = System.Text.Json.JsonDocument.Parse(output.ToString());
        Assert.Equal("cache-revalidated", result.RootElement.GetProperty("status").GetString());
        Assert.Empty(fixture.Calls());
    }

    [Theory]
    [InlineData("all")]
    [InlineData("unit")]
    public void RevalidateCommandDoesNotRequireAnObsoleteInstallationForVersionedResults(string layer)
    {
        using var fixture = new IncrementalTestServiceTests.Fixture();
        var registry = new MutationCommandRegistry();
        new TestingModule().RegisterCommands(registry, new IncrementalCommandServices(Service(), fixture));
        using var output = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        var previous = Console.Out;
        try
        {
            Console.SetOut(output);
            Assert.Equal(0, registry.Root.Parse(["test", "incremental", "revalidate", "--profile", "profile.json", "--repo", fixture.Path,
                "--reason", "Inspect the supported result contract without keeping an old installation.", "--layer", layer, "--format", "json"]).Invoke());
        }
        finally { Console.SetOut(previous); }
        using var result = System.Text.Json.JsonDocument.Parse(output.ToString());
        Assert.Equal("cache-revalidated", result.RootElement.GetProperty("status").GetString());
        Assert.False(result.RootElement.GetProperty("scopeComplete").GetBoolean());
        Assert.Empty(fixture.Calls());
    }
}

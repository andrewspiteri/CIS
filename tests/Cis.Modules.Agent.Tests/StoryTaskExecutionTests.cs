using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Agent;
using Cis.Modules.Repository;
using Cis.Providers.Agent.Codex;
using Cis.Providers.Agent.Claude;
using System.Diagnostics;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentExecutionTests
{
    [Fact]
    public void StoryExecutionForwardsSelectedModelsAndTaskReviewSchemaToProviders()
    {
        var request = Request("C:/bounded-worktree", approve: false) with { Model = "gpt-6-sol" };
        var codex = new ProcessStartInfo("codex"); CodexAgentProvider.ConfigureExecArguments(codex, request);
        Assert.Equal("gpt-6-sol", codex.ArgumentList[codex.ArgumentList.IndexOf("--model") + 1]);
        using var parameters = JsonDocument.Parse(JsonSerializer.Serialize(CodexAgentProvider.BuildThreadParameters(request, CisAgentPermissions.WorkspaceWrite)));
        Assert.Equal("gpt-6-sol", parameters.RootElement.GetProperty("model").GetString());
        var claude = new ProcessStartInfo("claude");
        ClaudeAgentProvider.ConfigureExecutionArguments(claude, request with { Model = "opus", Mode = "review", Permission = "read-only", TaskReview = true });
        Assert.Equal("opus", claude.ArgumentList[claude.ArgumentList.IndexOf("--model") + 1]);
        var schema = claude.ArgumentList[claude.ArgumentList.IndexOf("--json-schema") + 1];
        Assert.Contains("TASK-REV-", schema); Assert.DoesNotContain("BRD-REV-", schema);
        Assert.Contains("Read,Glob,Grep", claude.ArgumentList);
    }

    private static CisStoryTaskWork StoryWork(string root, bool complex = false) => new(root, "feature", "abcdef1234567890", "sha256:approved",
        "Deliver the approved behavior.", new("T1", complex ? "Enforce permissions" : "Update display text", "Implement this bounded change.",
            ["agent-fixture"], [1], ["The required behavior is implemented and checked."], []), ["The required behavior works."],
        [new("agent-fixture", root, "docs/cis", "authority")], "Reviewer");

    [Theory]
    [InlineData(false, "gpt-6-luna", "gpt-6-astra")]
    [InlineData(true, "gpt-6-astra", "gpt-6-sol")]
    public void StoryModelsFollowComplexityAndUseDistinctReviewer(bool complex, string implementation, string review)
    {
        using var repository = AgentRepository.Create();
        var provider = new StoryExecutionProvider();
        var service = new AgentService(new CisRepositoryContextResolver(), [provider], textGeneration: new StoryExecutionModels());
        var selected = service.SelectModels(StoryWork(repository.Path, complex));
        Assert.Empty(selected.Errors); Assert.Equal(implementation, selected.Implementation!.Model); Assert.Equal(review, selected.Review!.Model);
        Assert.Empty(provider.Requests);
        var unavailable = new AgentService(new CisRepositoryContextResolver(), [provider], textGeneration: new StoryExecutionModels(single: true));
        Assert.NotEmpty(unavailable.SelectModels(StoryWork(repository.Path, true)).Errors);
    }

    [Theory]
    [InlineData("ready")]
    [InlineData("findings")]
    [InlineData("concurrent")]
    [InlineData("cancelled")]
    [InlineData("no-consent")]
    [InlineData("malformed-review")]
    [InlineData("review-writes")]
    public void StoryExecutionRunsChosenModelsReviewsFrozenChangesAndPreservesOriginalOnFailure(string mode)
    {
        using var repository = AgentRepository.Create(git: true);
        repository.Write("app.txt", "original"); repository.CommitAll("task baseline");
        var provider = new StoryExecutionProvider { Findings = mode == "findings", Cancel = mode == "cancelled", MalformedReview = mode == "malformed-review", ReviewWrites = mode == "review-writes",
            DuringReview = mode == "concurrent" ? () => repository.Write("app.txt", "user edit") : null };
        var service = new AgentService(new CisRepositoryContextResolver(), [provider], textGeneration: new StoryExecutionModels());
        var work = StoryWork(repository.Path, true);
        var selection = service.SelectModels(work);
        var result = service.Execute(work, selection, mode != "no-consent", () => true, TestContext.Current.CancellationToken, null);
        if (mode == "no-consent") { Assert.Empty(provider.Requests); Assert.False(result.Applied); return; }
        if (mode == "cancelled") { Assert.Equal("cancelled", result.Status); Assert.False(result.Applied); return; }
        Assert.Equal(mode == "findings" ? 6 : 2, provider.Requests.Count);
        Assert.Equal("gpt-6-astra", provider.Requests[0].Model);
        Assert.Equal("gpt-6-sol", provider.Requests[1].Model);
        Assert.Equal("workspace-write", provider.Requests[0].Permission);
        Assert.Equal("read-only", provider.Requests[1].Permission);
        Assert.NotEqual(provider.Requests[0].WorkingDirectory, provider.Requests[1].WorkingDirectory);
        Assert.All(provider.Requests, request => Assert.NotEqual(repository.Path, request.WorkingDirectory));
        Assert.Equal(mode == "ready", result.Applied);
        Assert.Equal(mode == "ready" ? "implemented" : mode == "concurrent" ? "user edit" : "original", File.ReadAllText(Path.Combine(repository.Path, "app.txt")));
        if (mode == "findings") { Assert.Equal("changes-requested", result.Status); Assert.NotEmpty(result.Findings); }
        if (mode is "malformed-review" or "review-writes") Assert.Equal("review-failed", result.Status);
        if (mode == "ready") { Assert.Equal("reviewed", result.Status); Assert.Equal(new[] { "agent-fixture/app.txt" }, result.ChangedFiles); }
        Assert.All(result.Runs, run => Assert.False(string.IsNullOrWhiteSpace(run.RunId)));
        if (mode == "findings")
        {
            Assert.Contains(result.Errors, error => error.Contains("Two automatic correction rounds"));
            Assert.Equal(3, result.Runs.Max(run => run.Round));
            Assert.Equal("original", repository.Read("app.txt"));
        }
    }

    [Theory]
    [InlineData("ready")]
    [InlineData("blocked")]
    [InlineData("stale")]
    [InlineData("cancelled")]
    public void StoryReviewFindingsTriggerCorrectionAndFreshReviewWithinScope(string outcome)
    {
        using var repository = AgentRepository.Create(git: true);
        repository.Write("app.txt", "original"); repository.CommitAll("task baseline");
        var current = true;
        using var cancellation = new CancellationTokenSource();
        var provider = new StoryExecutionProvider { ReviewFailures = 1, Blocked = outcome == "blocked",
            DuringReview = () => { if (outcome == "stale") current = false; if (outcome == "cancelled") cancellation.Cancel(); } };
        var service = new AgentService(new CisRepositoryContextResolver(), [provider], textGeneration: new StoryExecutionModels());
        var work = StoryWork(repository.Path, true) with { ReviewResponses = [new("finding", "Which existing behavior?", "Keep the existing archive and retain versions.", "Reviewer", "2026-09-29")] };
        var result = service.Execute(work, service.SelectModels(work), true, () => current, cancellation.Token, null);
        Assert.All(provider.Requests, request => Assert.Contains("Keep the existing archive and retain versions.", request.Prompt));
        if (outcome != "ready")
        {
            Assert.Equal(outcome, result.Status); Assert.False(result.Applied);
            Assert.Equal(2, provider.Requests.Count); Assert.Equal("original", repository.Read("app.txt")); return;
        }
        Assert.Equal("reviewed", result.Status); Assert.True(result.Applied); Assert.Empty(result.Findings);
        Assert.Equal(new[] { "implement", "review", "implement", "review" }, provider.Requests.Select(request => request.Mode));
        Assert.Contains("Criterion not met", provider.Requests[2].Prompt);
        Assert.Contains("<reviewed-candidates>", provider.Requests[3].Prompt);
        Assert.Contains("Criterion not met", provider.Requests[3].Prompt);
        Assert.Contains("corrected", provider.Requests[3].Prompt);
        Assert.Equal(4, provider.Requests.Select(request => request.WorkingDirectory).Distinct().Count());
        Assert.Equal("implemented", File.ReadAllText(Path.Combine(provider.Requests[0].WorkingDirectory, "app.txt")));
        Assert.Equal("corrected", repository.Read("app.txt"));
        Assert.Equal(new[] { 1, 1, 2, 2 }, result.Runs.Select(run => run.Round));
        Assert.Equal(new[] { "app.txt" }, result.Runs[2].ChangedFiles);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void StoryExecutionHandlesRepositoryFreeReportsAndMultipleRepositories(int repositoryCount)
    {
        using var repository = AgentRepository.Create(git: true);
        using var second = AgentRepository.Create(git: true);
        foreach (var root in new[] { repository, second }) { root.Write("app.txt", "original"); root.CommitAll("task baseline"); }
        var work = StoryWork(repository.Path, true);
        CisWorkspaceRepository[] targets = repositoryCount == 0 ? [] : [work.Repositories[0], new("second", second.Path, "docs/cis", "authority")];
        work = work with { Repositories = targets, Task = work.Task with { RepositoryIds = targets.Select(target => target.Id).ToArray() } };
        var provider = new StoryExecutionProvider();
        var service = new AgentService(new CisRepositoryContextResolver(), [provider], textGeneration: new StoryExecutionModels());
        var result = service.Execute(work, service.SelectModels(work), true, () => true, TestContext.Current.CancellationToken, null);
        Assert.Empty(result.Errors); Assert.True(result.Applied);
        Assert.Equal(repositoryCount == 0 ? 2 : 4, provider.Requests.Count);
        Assert.Equal(repositoryCount == 0 ? "original" : "implemented", File.ReadAllText(Path.Combine(repository.Path, "app.txt")));
        Assert.Equal(repositoryCount == 0 ? "original" : "implemented", File.ReadAllText(Path.Combine(second.Path, "app.txt")));
        if (repositoryCount == 0) Assert.All(result.Runs, run => Assert.Equal("task-report", run.RepositoryId));
    }

    [Fact]
    public void StoryCorrectionReviewsAllRepositoriesAndSuppliesActualCrossRepositoryChanges()
    {
        using var first = AgentRepository.Create(git: true);
        using var second = AgentRepository.Create(git: true);
        foreach (var root in new[] { first, second }) { root.Write("app.txt", "original"); root.CommitAll("baseline"); }
        var work = StoryWork(first.Path, true) with { Repositories = [new("first", first.Path, "docs/cis", "authority"), new("second", second.Path, "docs/cis", "participant")] };
        var provider = new StoryExecutionProvider { ReviewFailures = 1, DuringReview = () =>
            { Assert.Equal("original", first.Read("app.txt")); Assert.Equal("original", second.Read("app.txt")); } };
        var service = new AgentService(new CisRepositoryContextResolver(), [provider], textGeneration: new StoryExecutionModels());
        var result = service.Execute(work, service.SelectModels(work), true, () => true, TestContext.Current.CancellationToken, null);
        Assert.True(result.Applied, string.Join("; ", result.Errors));
        Assert.Equal(new[] { "implement", "implement", "review", "review", "implement", "implement", "review", "review" }, provider.Requests.Select(request => request.Mode));
        Assert.All(provider.Requests.Where(request => request.Mode == "review"), request =>
        {
            Assert.Contains("\"id\": \"first\"", request.Prompt);
            Assert.Contains("\"id\": \"second\"", request.Prompt);
            Assert.Contains("diff --git", request.Prompt);
        });
        Assert.Equal("corrected", first.Read("app.txt")); Assert.Equal("corrected", second.Read("app.txt"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StoryExecutionSupportsUnbornBranchesWithoutCreatingUserCommitsOrChangingIndex(bool empty)
    {
        using var authority = AgentRepository.Create(git: true);
        using var target = AgentRepository.Create(unborn: true);
        if (empty)
        {
            foreach (var file in new[] { ".cis/repository.yml", "docs/cis/catalog.yml", "docs/cis/references/agent-provider-profile.md", ".gitignore" })
                File.Delete(Path.Combine(target.Path, file));
        }
        else
        {
            target.Write("app.txt", "staged"); target.Git("add", "app.txt"); target.Write("app.txt", "unstaged");
            target.Write("ignored.txt", "ignored"); target.Write(".gitignore", ".cis/local/\nignored.txt\n");
        }
        var indexPath = Path.Combine(target.Path, ".git/index");
        var index = File.Exists(indexPath) ? File.ReadAllBytes(indexPath) : null;
        var branch = target.Git("symbolic-ref", "HEAD");
        var work = StoryWork(authority.Path) with { Repositories = [new("agent-fixture", target.Path, "docs/cis", "participant")] };
        var provider = new StoryExecutionProvider();
        var service = new AgentService(new CisRepositoryContextResolver(), [provider], textGeneration: new StoryExecutionModels());
        var result = service.Execute(work, service.SelectModels(work), true, () => true, TestContext.Current.CancellationToken, null);
        Assert.True(result.Applied, string.Join("; ", result.Errors));
        Assert.Equal("implemented", target.Read("app.txt"));
        Assert.Equal(branch, target.Git("symbolic-ref", "HEAD"));
        Assert.Throws<InvalidOperationException>(() => target.Git("rev-parse", "--verify", "HEAD"));
        Assert.Equal(index, File.Exists(indexPath) ? File.ReadAllBytes(indexPath) : null);
        Assert.All(provider.Requests, request => Assert.False(File.Exists(Path.Combine(request.WorkingDirectory, "ignored.txt"))));
    }

    [Fact]
    public void LargeStoryEvidenceIsCompleteBoundedAndAvailableToEveryReviewAndCorrection()
    {
        using var first = AgentRepository.Create(git: true);
        using var second = AgentRepository.Create(git: true);
        foreach (var root in new[] { first, second }) { root.Write("app.txt", "original"); root.CommitAll("baseline"); }
        var payload = string.Concat(Enumerable.Repeat("inventory <ownership> 😀\n", 4_000)) + "END-OF-INVENTORY";
        var work = StoryWork(first.Path, true) with { Repositories = [new("first", first.Path, "docs/cis", "authority"), new("second", second.Path, "docs/cis", "participant")] };
        var evidenceReads = 0;
        var provider = new StoryExecutionProvider { LargeEvidence = payload, ReviewFailures = 1, InspectRequest = request =>
        {
            if (request.Mode != "review" && !request.Prompt.Contains("<review-findings>")) return;
            evidenceReads++;
            Assert.True(request.Prompt.Length < 50_000);
            Assert.Contains("EVERY repository", request.Prompt);
            var packets = ReadStoryEvidencePackets(request);
            Assert.Equal(new[] { "first", "second" }, packets.Select(packet => packet.Id));
            foreach (var packet in packets)
            {
                Assert.Contains("diff --git", packet.Text);
                Assert.Contains("+" + payload.Replace("\n", "\n+"), packet.Text);
                Assert.Contains("END-OF-INVENTORY", packet.Text);
                Assert.Contains(request.Prompt.Contains("earlier findings") && request.Prompt.Contains("Criterion not met") ? "+corrected" : "+implemented", packet.Text);
            }
        } };
        var service = new AgentService(new CisRepositoryContextResolver(), [provider], textGeneration: new StoryExecutionModels());
        var result = service.Execute(work, service.SelectModels(work), true, () => true, TestContext.Current.CancellationToken, null);
        Assert.True(result.Applied, string.Join("; ", result.Errors));
        Assert.Equal(6, evidenceReads);
        Assert.Equal("corrected", first.Read("app.txt")); Assert.Equal(payload, second.Read("inventory.txt"));
        Assert.DoesNotContain(result.ChangedFiles, file => file.Contains("story-evidence"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChangedOrMissingLargeReviewEvidencePreventsApplicationAndRetryReusesImplementation(bool delete)
    {
        using var repository = AgentRepository.Create(git: true);
        repository.Write("app.txt", "original"); repository.CommitAll("baseline");
        var tamper = true;
        var provider = new StoryExecutionProvider { LargeEvidence = new string('x', 220_000), InspectRequest = request =>
        {
            if (request.Mode != "review") return;
            var packets = ReadStoryEvidencePackets(request);
            Assert.Single(packets);
            if (!tamper) return;
            tamper = false;
            if (delete) File.Delete(packets[0].Paths[0]); else File.AppendAllText(packets[0].Paths[0], "modified");
        } };
        var service = new AgentService(new CisRepositoryContextResolver(), [provider], textGeneration: new StoryExecutionModels());
        var work = StoryWork(repository.Path, true);
        var selection = service.SelectModels(work);
        var failed = service.Execute(work, selection, true, () => true, TestContext.Current.CancellationToken, null);
        Assert.False(failed.Applied); Assert.Equal("failed", failed.Status);
        Assert.Contains(failed.Errors, error => error.Contains("frozen task review evidence"));
        Assert.Equal("original", repository.Read("app.txt"));
        var retry = service.Execute(work with { PreviousExecution = failed }, selection, true, () => true, TestContext.Current.CancellationToken, null);
        Assert.True(retry.Applied, string.Join("; ", retry.Errors));
        Assert.Single(provider.Requests, request => request.Mode == "implement");
    }

    [Fact]
    public void LargeReviewEvidenceStillRejectsCredentialShapedChangesBeforeReview()
    {
        using var repository = AgentRepository.Create(git: true);
        var provider = new StoryExecutionProvider { LargeEvidence = new string('x', 220_000) + "\npassword = \"do-not-send\"" };
        var service = new AgentService(new CisRepositoryContextResolver(), [provider], textGeneration: new StoryExecutionModels());
        var work = StoryWork(repository.Path, true);
        var result = service.Execute(work, service.SelectModels(work), true, () => true, TestContext.Current.CancellationToken, null);
        Assert.False(result.Applied);
        Assert.Contains(result.Errors, error => error.Contains("credential-shaped"));
        Assert.Single(provider.Requests);
    }

    private static List<(string Id, string Text, string[] Paths)> ReadStoryEvidencePackets(CisAgentExecutionRequest request)
    {
        var tag = request.Mode == "review" ? "reviewed-candidates" : "previous-candidates";
        var start = request.Prompt.IndexOf("<" + tag + ">", StringComparison.Ordinal);
        var jsonStart = request.Prompt.IndexOf('[', start);
        var end = request.Prompt.IndexOf("</" + tag + ">", jsonStart, StringComparison.Ordinal);
        using var index = JsonDocument.Parse(request.Prompt[jsonStart..end]);
        var packets = new List<(string, string, string[])>();
        Assert.Single(request.EvidenceDirectories);
        var claude = new ProcessStartInfo("claude");
        ClaudeAgentProvider.ConfigureExecutionArguments(claude, request);
        Assert.Equal(request.EvidenceDirectories[0], claude.ArgumentList[claude.ArgumentList.IndexOf("--add-dir") + 1]);
        if (request.Mode == "review")
        {
            Assert.Contains("--restricted", claude.ArgumentList);
            Assert.Equal("Read,Glob,Grep", claude.ArgumentList[claude.ArgumentList.IndexOf("--tools") + 1]);
        }
        foreach (var repository in index.RootElement.EnumerateArray())
        {
            var text = ""; var paths = new List<string>();
            foreach (var part in repository.GetProperty("parts").EnumerateArray())
            {
                var path = part.GetProperty("path").GetString()!;
                Assert.True(Path.IsPathFullyQualified(path));
                Assert.Equal(request.EvidenceDirectories[0], Path.GetDirectoryName(path));
                var content = File.ReadAllText(path);
                Assert.InRange(content.Length, 1, 48_000);
                Assert.Equal(part.GetProperty("characters").GetInt32(), content.Length);
                Assert.Equal(part.GetProperty("sha256").GetString(), Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))));
                text += content; paths.Add(path);
            }
            packets.Add((repository.GetProperty("id").GetString()!, text, paths.ToArray()));
        }
        return packets;
    }

    [Fact]
    public void StoryExecutionPreparesEveryRepositoryBeforeCallingAnyModel()
    {
        using var first = AgentRepository.Create(git: true);
        using var invalid = AgentRepository.Create();
        var work = StoryWork(first.Path) with { Repositories = [new("first", first.Path, "docs/cis", "authority"), new("invalid", invalid.Path, "docs/cis", "participant")] };
        var provider = new StoryExecutionProvider();
        var service = new AgentService(new CisRepositoryContextResolver(), [provider], textGeneration: new StoryExecutionModels());
        var result = service.Execute(work, service.SelectModels(work), true, () => true, TestContext.Current.CancellationToken, null);
        Assert.False(result.Applied); Assert.NotEmpty(result.Errors); Assert.Empty(provider.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StoryRetryReusesSuccessfulImplementationButRejectsChangedRepositoryBaseline(bool changeBaseline)
    {
        using var first = AgentRepository.Create(git: true);
        using var second = AgentRepository.Create(git: true);
        first.Write("app.txt", "original"); first.CommitAll("task baseline");
        second.Write("app.txt", "original"); second.CommitAll("task baseline");
        var work = StoryWork(first.Path) with { Repositories = [new("first", first.Path, "docs/cis", "authority"), new("second", second.Path, "docs/cis", "participant")] };
        var provider = new StoryExecutionProvider { FailImplementationNumber = 2 };
        var service = new AgentService(new CisRepositoryContextResolver(), [provider], textGeneration: new StoryExecutionModels());
        var selection = service.SelectModels(work);
        var failed = service.Execute(work, selection, true, () => true, TestContext.Current.CancellationToken, null);
        Assert.False(failed.Applied); Assert.Equal("failed", failed.Status);
        Assert.Equal("original", first.Read("app.txt"));
        var completed = failed.Runs.Single(run => run.Status == CisAgentRunStates.Succeeded);
        if (changeBaseline) first.Write("app.txt", "user edit");
        var retried = service.Execute(work with { PreviousExecution = failed }, selection, true, () => true, TestContext.Current.CancellationToken, null);
        if (changeBaseline)
        {
            Assert.False(retried.Applied); Assert.Contains(retried.Errors, error => error.Contains("baseline changed"));
            Assert.Equal(2, provider.Requests.Count); Assert.Equal("user edit", first.Read("app.txt"));
        }
        else
        {
            Assert.True(retried.Applied, string.Join("; ", retried.Errors));
            Assert.Equal(3, provider.Requests.Count(request => request.Mode == "implement"));
            Assert.Equal(2, provider.Requests.Count(request => request.Mode == "review"));
            Assert.Contains(retried.Runs, run => run.RunId == completed.RunId);
            Assert.Equal("implemented", first.Read("app.txt")); Assert.Equal("implemented", second.Read("app.txt"));
        }
    }

    private sealed class StoryExecutionModels(bool single = false) : ICisTextGenerationService
    {
        public CisAiStatus GetStatus() => new([new("codex", "available", "test", false,
            single ? [new("gpt-6-astra")] : [new("gpt-6-astra"), new("gpt-6-sol"), new("gpt-6-luna")], null)]);
        public CisTextGenerationResult Generate(CisTextGenerationRequest request) => throw new InvalidOperationException("Model choice must not generate text.");
    }

    private sealed class StoryExecutionProvider : ICisAgentProvider
    {
        public List<CisAgentExecutionRequest> Requests { get; } = [];
        public bool Findings { get; init; }
        public int ReviewFailures { get; init; }
        public bool Blocked { get; init; }
        public bool Cancel { get; init; }
        public bool MalformedReview { get; init; }
        public bool ReviewWrites { get; init; }
        public int? FailImplementationNumber { get; init; }
        public Action? DuringReview { get; init; }
        public string? LargeEvidence { get; init; }
        public Action<CisAgentExecutionRequest>? InspectRequest { get; init; }
        public CisAgentProviderDescriptor Descriptor => new("codex", "Test coding agent", "test", true, ["exec-json"], ["implement", "review"], ["read-only", "workspace-write"], false, false, "Test only");
        public CisAgentProviderDiagnosis Diagnose(string repositoryPath) => new("codex", "ready", true, null, "test", true, [], []);
        public CisAgentProviderExecutionResult Execute(CisAgentExecutionRequest request, Action<CisAgentProviderEvent> onEvent, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (request.Mode == "implement" && Requests.Count(item => item.Mode == "implement") == FailImplementationNumber)
                return new(CisAgentRunStates.Failed, 1, null, "Provider unavailable", [], [], [], null, null, null, null, ["Provider unavailable"]);
            if (Cancel) throw new OperationCanceledException(cancellationToken);
            if (request.Mode == "implement") File.WriteAllText(Path.Combine(request.WorkingDirectory, "app.txt"),
                ReviewFailures > 0 && request.Prompt.Contains("<review-findings>") ? "corrected" : "implemented");
            else
            {
                Assert.Contains(File.ReadAllText(Path.Combine(request.WorkingDirectory, "app.txt")), new[] { "implemented", "corrected" }); DuringReview?.Invoke();
                if (ReviewWrites) File.WriteAllText(Path.Combine(request.WorkingDirectory, "app.txt"), "review edited");
                if (MalformedReview) return new(CisAgentRunStates.Succeeded, 0, null, "not structured review", [], [], [], null, null, null, null, []);
            }
            if (request.Mode == "implement" && LargeEvidence is not null) File.WriteAllText(Path.Combine(request.WorkingDirectory, "inventory.txt"), LargeEvidence);
            InspectRequest?.Invoke(request);
            var needsChanges = Findings || Blocked || Requests.Count(item => item.Mode == "review") <= ReviewFailures;
            var completion = new { summary = request.Mode == "implement" ? "Implemented task." : "Reviewed actual implementation.",
                changedFiles = request.Mode == "implement" ? LargeEvidence is null ? new[] { "app.txt" } : ["app.txt", "inventory.txt"] : [], validations = new[] { "Fixture validation passed." }, evidence = new[] { "app.txt" },
                review = request.Mode == "review" ? new { recommendation = Blocked ? "blocked" : needsChanges ? "revise" : "ready", strengths = new[] { "Scoped change." },
                    findings = needsChanges ? new[] { new { id = "TASK-REV-001", severity = "major", category = "acceptance", location = "app.txt", observation = "Criterion not met.", recommendation = "Correct the behavior." } } : [] } : null };
            return new(CisAgentRunStates.Succeeded, 0, null, JsonSerializer.Serialize(completion), [], [], [], null, null, null, null, []);
        }
    }
}

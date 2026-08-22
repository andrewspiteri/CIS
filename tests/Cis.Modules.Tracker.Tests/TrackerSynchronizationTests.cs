using Cis.Abstractions;
using Cis.Host;
using Cis.Modules.Repository;
using Cis.Modules.Tracker;

namespace Cis.Modules.Tracker.Tests;

public sealed class TrackerSynchronizationTests
{
    [Fact]
    public void TrackerCommands_AreRegistered()
    {
        using var application = new CisHostBuilder()
            .AddModule(new RepositoryModule())
            .AddModule(new TrackerModule())
            .Build();

        Assert.Equal(0, application.Invoke(["tracker", "plan", "--help"]));
        Assert.Equal(0, application.Invoke(["tracker", "push", "--help"]));
        Assert.Equal(0, application.Invoke(["tracker", "pull", "--help"]));
        Assert.Equal(0, application.Invoke(["tracker", "status", "--help"]));
        Assert.Equal(0, application.Invoke(["tracker", "resolve", "--help"]));
    }

    [Fact]
    public void Push_CreatesDurableLinkAndBecomesIdempotent()
    {
        using var repository = TrackerRepository.Create();
        var provider = new FakeProvider();
        var service = Service(provider);

        var first = service.Push(repository.Path, "CIS-0001", "work");
        var second = service.Push(repository.Path, "CIS-0001", "work");

        Assert.Equal(0, first.ExitCode);
        Assert.Equal("create", Assert.Single(first.Items).Action);
        Assert.True(Assert.Single(first.Items).Applied);
        Assert.Equal("unchanged", Assert.Single(second.Items).Action);
        Assert.Equal(1, provider.Created);
        Assert.Contains("| work | 1 |", File.ReadAllText(repository.TaskPath));
        Assert.True(File.Exists(System.IO.Path.Combine(repository.Path, ".cis", "local", "trackers", "state.json")));
    }

    [Fact]
    public void DurableTaskLink_RecoversMappingAfterDisposableStateDeletion()
    {
        using var repository = TrackerRepository.Create();
        var provider = new FakeProvider(); var service = Service(provider);
        service.Push(repository.Path, "CIS-0001", "work");
        var state = System.IO.Path.Combine(repository.Path, ".cis", "local", "trackers", "state.json");
        File.Delete(state);

        var status = service.Status(repository.Path);
        var push = service.Push(repository.Path, "CIS-0001", "work");

        Assert.Single(status.Mappings);
        Assert.Equal("unchanged", Assert.Single(push.Items).Action);
        Assert.Equal(1, provider.Created);
        Assert.True(File.Exists(state));
    }

    [Fact]
    public void Push_UpdatesRemoteWhenOnlyCanonicalProjectionChanged()
    {
        using var repository = TrackerRepository.Create();
        var provider = new FakeProvider(); var service = Service(provider);
        service.Push(repository.Path, "CIS-0001", "work");
        File.WriteAllText(repository.TaskPath, File.ReadAllText(repository.TaskPath).Replace("Implement the bounded change.", "Implement the reviewed bounded change.", StringComparison.Ordinal));

        var plan = service.Plan(repository.Path, "CIS-0001", "work");
        var push = service.Push(repository.Path, "CIS-0001", "work");

        Assert.Equal("update", Assert.Single(plan.Items).Action);
        Assert.Equal("update", Assert.Single(push.Items).Action);
        Assert.Equal(1, provider.Updated);
    }

    [Fact]
    public void Pull_PersistsRemoteDriftWithoutChangingCanonicalTask()
    {
        using var repository = TrackerRepository.Create();
        var provider = new FakeProvider(); var service = Service(provider);
        service.Push(repository.Path, "CIS-0001", "work");
        var canonical = File.ReadAllText(repository.TaskPath);
        provider.EditRemote("1", title: "Remote title edit");

        var result = service.Pull(repository.Path, "CIS-0001", "work");

        Assert.Equal(5, result.ExitCode);
        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal("remote-changed", conflict.Kind);
        Assert.Equal(canonical, File.ReadAllText(repository.TaskPath));
        Assert.True(File.Exists(System.IO.Path.Combine(repository.Path, ".cis", "local", "trackers", "conflicts.json")));
    }

    [Fact]
    public void Pull_DetectsConcurrentChangesAndRemoteDeletion()
    {
        using var repository = TrackerRepository.Create();
        var provider = new FakeProvider(); var service = Service(provider);
        service.Push(repository.Path, "CIS-0001", "work");
        File.WriteAllText(repository.TaskPath, File.ReadAllText(repository.TaskPath).Replace("Implement the bounded change.", "Canonical edit.", StringComparison.Ordinal));
        provider.EditRemote("1", title: "Remote edit");

        var concurrent = service.Pull(repository.Path, "CIS-0001", "work");
        Assert.Equal("both-changed", Assert.Single(concurrent.Conflicts).Kind);

        provider.Delete("1");
        var deleted = service.Pull(repository.Path, "CIS-0001", "work");
        Assert.Equal("remote-deleted", Assert.Single(deleted.Conflicts).Kind);
        Assert.Equal(1, provider.Created);
    }

    [Fact]
    public void ResolveWithCisAuthority_ForcesAReviewedRemoteUpdate()
    {
        using var repository = TrackerRepository.Create();
        var provider = new FakeProvider(); var service = Service(provider);
        service.Push(repository.Path, "CIS-0001", "work");
        provider.EditRemote("1", title: "Unreviewed remote title");
        service.Pull(repository.Path, "CIS-0001", "work");

        var resolved = service.Resolve(repository.Path, "CIS-0001", "WORK-001", "work", "cis", "andrew", "Canonical contract is approved.");
        var pushed = service.Push(repository.Path, "CIS-0001", "work");

        Assert.Equal(0, resolved.ExitCode);
        Assert.Equal("update", Assert.Single(pushed.Items).Action);
        Assert.Contains("Canonical contract is approved.", File.ReadAllText(repository.TaskPath));
    }

    [Fact]
    public void MissingLoadedProvider_IsReportedWithoutMutation()
    {
        using var repository = TrackerRepository.Create(kind: "github");
        var service = Service(new FakeProvider());

        var result = service.Push(repository.Path, "CIS-0001", "work");

        Assert.Equal(4, result.ExitCode);
        Assert.Contains(result.Errors, error => error.Contains("github", StringComparison.Ordinal));
        Assert.DoesNotContain("| work | 1 |", File.ReadAllText(repository.TaskPath));
    }

    [Fact]
    public void InvalidLocalState_FailsClosedInsteadOfCreatingDuplicateIssues()
    {
        using var repository = TrackerRepository.Create();
        var provider = new FakeProvider(); var service = Service(provider);
        var state = System.IO.Path.Combine(repository.Path, ".cis", "local", "trackers", "state.json");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(state)!);
        File.WriteAllText(state, "{ invalid");

        var result = service.Push(repository.Path, "CIS-0001", "work");

        Assert.Equal(4, result.ExitCode);
        Assert.Contains(result.Errors, error => error.Contains("invalid", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, provider.Created);
    }

    [Fact]
    public void FailedRemoteUpdate_PreservesBaselineAndCanBeRetriedSafely()
    {
        using var repository = TrackerRepository.Create();
        var provider = new FakeProvider(); var service = Service(provider);
        service.Push(repository.Path, "CIS-0001", "work");
        File.WriteAllText(repository.TaskPath, File.ReadAllText(repository.TaskPath)
            .Replace("Implement the bounded change.", "Implement the retry-safe change.", StringComparison.Ordinal));
        provider.FailNextUpdate();

        var failed = service.Push(repository.Path, "CIS-0001", "work");
        var retried = service.Push(repository.Path, "CIS-0001", "work");

        Assert.Equal(4, failed.ExitCode);
        Assert.Equal("failed", Assert.Single(failed.Items).Action);
        Assert.Equal(0, retried.ExitCode);
        Assert.Equal("update", Assert.Single(retried.Items).Action);
        Assert.Equal(1, provider.Updated);
    }

    private static TrackerService Service(ICisTrackerProvider provider)
        => new(new CisRepositoryContextResolver(), new TrackerProviderRegistry([provider]), () => DateTimeOffset.Parse("2026-08-14T12:00:00Z"));

    private sealed class FakeProvider : ICisTrackerProvider
    {
        private readonly Dictionary<string, TrackerRemoteItem> _items = new(StringComparer.Ordinal);
        public string Kind => "fake";
        public int Created { get; private set; }
        public int Updated { get; private set; }
        private bool _failNextUpdate;
        public TrackerAvailability Probe(TrackerProviderConfiguration configuration) => new(true, "available");
        public TrackerRemoteItem? Get(TrackerProviderConfiguration configuration, string remoteId) => _items.GetValueOrDefault(remoteId);
        public TrackerRemoteItem Create(TrackerProviderConfiguration configuration, TrackerTaskProjection task)
        {
            Created++; var id = Created.ToString();
            return _items[id] = new(id, $"https://tracker.test/{id}", task.Title, task.Body, "open", task.Labels, "2026-08-14T12:00:00Z", "");
        }
        public TrackerRemoteItem Update(TrackerProviderConfiguration configuration, string remoteId, TrackerTaskProjection task)
        {
            if (_failNextUpdate) { _failNextUpdate = false; throw new InvalidOperationException("simulated rate limit"); }
            Updated++;
            return _items[remoteId] = new(remoteId, $"https://tracker.test/{remoteId}", task.Title, task.Body, "open", task.Labels, "2026-08-14T12:00:00Z", "");
        }
        public void EditRemote(string id, string title)
        {
            var item = _items[id]; _items[id] = item with { Title = title, UpdatedAt = "2026-08-14T12:05:00Z", Digest = "" };
        }
        public void Delete(string id) => _items.Remove(id);
        public void FailNextUpdate() => _failNextUpdate = true;
    }

    private sealed class TrackerRepository : IDisposable
    {
        public string Path { get; }
        public string TaskPath => System.IO.Path.Combine(Path, "docs", "cis", "changes", "CIS-0001", "agent-tasks", "WORK-001.md");
        private TrackerRepository(string path) => Path = path;
        public static TrackerRepository Create(string kind = "fake")
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cis-tracker-tests", Guid.NewGuid().ToString("N"));
            Write(root, ".cis/repository.yml", "schema_version: 1\nrepository:\n  id: tracker-fixture\ndocumentation_root: docs/cis\n");
            Write(root, "docs/cis/catalog.yml", "version: 1\nrepository: tracker-fixture\ndocuments: []\n");
            Write(root, "docs/cis/references/external-tracker-profile.md", $"""
                # External Tracker Profile
                | Provider | Kind | Enabled | Target | Base URL | Direction | Issue type | Credential source |
                |---|---|---|---|---|---|---|---|
                | work | {kind} | yes | team/project | https://tracker.test | cis-to-remote | task | environment |
                """);
            Write(root, "docs/cis/changes/CIS-0001/agent-tasks/WORK-001.md", """
                ---
                title: "WORK-001 Implement feature"
                type: agent-task
                status: Draft
                task_status: NotStarted
                task_id: WORK-001
                task_type: core.backend.behavior
                task_type_version: 1.0
                category: backend
                complexity: medium
                authority: human-approved-plan
                ---

                # WORK-001: Implement feature

                ## Objective

                Implement the bounded change.

                ## Required changes

                - Preserve the reviewed requirement.

                ## Required outputs

                - Working behavior.

                ## Constraints and exclusions

                - No unreviewed scope.

                ## Dependencies and approval gates

                - Human plan approval.

                ## Acceptance criteria

                - [ ] Reviewed behavior passes.

                ## Targeted validation

                - [ ] Focused tests pass.

                ## Deferrals and residual risk

                - None.

                ## External issue links

                | Provider | Remote ID | URL | Canonical digest | Remote digest | Last synchronized UTC | State |
                |---|---|---|---|---|---|---|

                ## External synchronization decisions

                | Provider | Decision | Reviewer | Timestamp UTC | Rationale |
                |---|---|---|---|---|
                """);
            return new(root);
        }
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
        private static void Write(string root, string relative, string content)
        {
            var path = System.IO.Path.Combine(root, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!); File.WriteAllText(path, content);
        }
    }
}

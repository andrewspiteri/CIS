using Cis.Abstractions;

namespace Cis.Modules.Repository.Tests;

public sealed class WorkspaceSnapshotTests
{
    [Fact]
    public void SnapshotSharesChecksAcrossProjectionsAndRechecksOnNextRequest()
    {
        var dispatcher = new CheckedDispatcher();
        var root = Path.GetFullPath("snapshot-test");
        var first = WorkspaceSnapshot.Read(dispatcher, root);
        Assert.Equal(15, first.Entries.Count);
        Assert.Equal(1, dispatcher.Checks);
        Assert.All(first.Entries.Where(entry => entry.ExitCode == 0), entry =>
            Assert.Equal(1, entry.Data!.Value.GetProperty("checkedValue").GetInt32()));
        var blocked = Assert.Single(first.Entries, entry => entry.ExitCode != 0);
        Assert.Equal(5, blocked.ExitCode);
        Assert.Equal("blocked", blocked.Data!.Value.GetProperty("status").GetString());
        Assert.Equal("existing finding", blocked.StandardError);
        Assert.All(dispatcher.Arguments, arguments =>
        {
            Assert.Equal(root, arguments[^3]);
            Assert.Equal("--format", arguments[^2]);
            Assert.Equal("json", arguments[^1]);
        });
        dispatcher.Value = 2;
        var second = WorkspaceSnapshot.Read(dispatcher, root);
        Assert.Equal(2, dispatcher.Checks);
        Assert.Equal(2, second.Entries[0].Data!.Value.GetProperty("checkedValue").GetInt32());
        // No ambient snapshot may leak to a subsequent individual command.
        dispatcher.Capture(["brd", "status", "--workspace", root, "--format", "json"]);
        Assert.Equal(3, dispatcher.Checks);
    }

    [Theory]
    [InlineData("human")]
    [InlineData("json")]
    [InlineData("agent")]
    public void SnapshotCommandIsRegisteredAndRejectsCallerSuppliedCommands(string format)
    {
        using var application = new Cis.Host.CisHostBuilder().AddModule(new WorkspaceModule()).Build();
        Assert.Equal(0, application.Invoke(["workspace", "snapshot", "--help"]));
        Assert.NotEqual(0, application.Invoke(["workspace", "snapshot", "--format", format, "definition", "prepare"]));
        Assert.Equal(2, application.Invoke(["workspace", "snapshot", "--format", "xml"]));
    }

    private sealed class CheckedDispatcher : ICisCommandDispatcher
    {
        public int Checks { get; private set; }
        public int Value { get; set; } = 1;
        public List<string[]> Arguments { get; } = [];
        public CisCommandCapture Capture(string[] arguments)
        {
            Arguments.Add(arguments);
            var value = CisReadScope.Read(this, "check", arguments[^3], () => { Checks++; return Value; });
            return arguments[0] == "repo"
                ? new(5, "{\"status\":\"blocked\"}", "existing finding")
                : new(0, $"{{\"checkedValue\":{value}}}", string.Empty);
        }
    }
}

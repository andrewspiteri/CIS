using System.CommandLine;
using System.Diagnostics;
using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Repository;

/// <summary>A bounded read-only projection; no caller-supplied command can enter its read scope.</summary>
internal static class WorkspaceSnapshot
{
    // Scope is part of the query identity. Keep result payloads and exit codes owned by
    // their original command; the extension only transports and displays these results.
    private static readonly (string[] Arguments, string Scope)[] Queries =
    [
        (["brd", "status"], "--workspace"),
        (["technical-intent", "questions", "status", "--summary"], "--workspace"),
        (["technical-intent", "status"], "--workspace"),
        (["change", "list"], "--repo"),
        (["agent", "providers"], "--repo"),
        (["agent", "runs", "--summary", "--limit", "10"], "--repo"),
        (["repo", "doctor"], "--repo"),
        (["skills", "inventory", "--summary"], "--repo"),
        (["brd", "questions", "list"], "--workspace"),
        (["standards", "inventory", "--summary"], "--repo"),
        (["references", "validate"], "--repo"),
        (["agent", "runs", "--change", "PRODUCT", "--summary", "--latest-per-task", "--limit", "10"], "--repo"),
        (["definition", "status"], "--workspace"),
        (["brd", "questions", "guidance"], "--workspace"),
        (["brd", "feature", "wizard", "navigation"], "--workspace"),
    ];

    internal static Command CreateCommand(ICisCommandDispatcher dispatcher)
    {
        var command = new Command("snapshot", "Read startup projections using one checked workspace read scope.");
        var repo = new Option<string>("--repo") { DefaultValueFactory = _ => Directory.GetCurrentDirectory() };
        var format = new Option<string>("--format") { DefaultValueFactory = _ => "human" };
        command.Options.Add(repo);
        command.Options.Add(format);
        command.SetAction(parse =>
        {
            var selectedFormat = (parse.GetValue(format) ?? "human").ToLowerInvariant();
            if (selectedFormat is not ("human" or "json" or "agent"))
            {
                Console.Error.WriteLine("Unsupported format. Expected human, json, or agent.");
                return 2;
            }
            var repository = Path.GetFullPath(parse.GetValue(repo) ?? Directory.GetCurrentDirectory());
            var result = Read(dispatcher, repository);
            if (selectedFormat == "json")
                Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            else
            {
                Console.WriteLine(selectedFormat == "agent"
                    ? $"schemaVersion=1;status=complete;queries={result.Entries.Count};durationMs={result.DurationMs:F1}"
                    : $"Workspace snapshot: {result.Entries.Count} queries in {result.DurationMs:F1} ms");
                foreach (var entry in result.Entries)
                    Console.WriteLine(selectedFormat == "agent"
                        ? $"query={string.Join(' ', entry.Arguments)};exitCode={entry.ExitCode};durationMs={entry.DurationMs:F1}"
                        : $"  {string.Join(' ', entry.Arguments)}: exit {entry.ExitCode}, {entry.DurationMs:F1} ms");
            }
            // An individual blocked/failed projection remains an individual result.
            return 0;
        });
        return command;
    }

    internal static Snapshot Read(ICisCommandDispatcher dispatcher, string repository)
    {
        var started = DateTimeOffset.UtcNow;
        var watch = Stopwatch.StartNew();
        using var scope = CisReadScope.Enter();
        var entries = new List<Entry>();
        foreach (var (arguments, scopeOption) in Queries)
        {
            var queryWatch = Stopwatch.StartNew();
            var capture = dispatcher.Capture([.. arguments, scopeOption, repository, "--format", "json"]);
            JsonElement? data = null;
            try
            {
                using var document = JsonDocument.Parse(capture.StandardOutput);
                if (document.RootElement.ValueKind == JsonValueKind.Object) data = document.RootElement.Clone();
            }
            catch (JsonException) { /* Preserve the diagnostic and exit code for unavailable projections. */ }
            entries.Add(new(arguments, scopeOption, capture.ExitCode, data, capture.StandardError, queryWatch.Elapsed.TotalMilliseconds));
        }
        return new(1, repository, started, watch.Elapsed.TotalMilliseconds, entries);
    }

    internal sealed record Snapshot(int SchemaVersion, string RepositoryPath, DateTimeOffset CheckedAt, double DurationMs, IReadOnlyList<Entry> Entries);
    internal sealed record Entry(string[] Arguments, string Scope, int ExitCode, JsonElement? Data, string StandardError, double DurationMs);
}

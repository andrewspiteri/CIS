using System.CommandLine;
using System.Text.Json;
using Cis.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Cis.Modules.Tracker;

public sealed class TrackerModule : ICisModule
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public string Name => "tracker";
    public string Description => "Project canonical CIS tasks into external issue trackers and reconcile drift safely.";

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<TrackerProviderRegistry>();
        services.AddSingleton<TrackerService>();
        services.AddSingleton<ICisRepositoryDoctorCheck, TrackerDoctorCheck>();
    }

    public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services)
    {
        var service = services.GetRequiredService<TrackerService>();
        var tracker = new Command(Name, Description);
        tracker.Subcommands.Add(Sync("plan", "Preview external issue creation, updates, and conflicts without writing.", service.Plan));
        tracker.Subcommands.Add(Sync("push", "Create or update remote issues from canonical CIS tasks; conflicts are never overwritten.", service.Push));
        tracker.Subcommands.Add(Sync("pull", "Read remote issues and persist drift conflicts without changing canonical task authority.", service.Pull));
        tracker.Subcommands.Add(Status(service));
        tracker.Subcommands.Add(Resolve(service));
        commands.Add(tracker);
    }

    private static Command Sync(string name, string description, Func<string, string, string?, TrackerSyncResult> action)
    {
        var command = new Command(name, description);
        var change = new Argument<string>("change-id");
        var provider = new Option<string?>("--provider") { Description = "Configured provider key; defaults to every enabled provider." };
        var repo = Repo(); var format = Format();
        command.Arguments.Add(change); command.Options.Add(provider); command.Options.Add(repo); command.Options.Add(format);
        command.SetAction(parse =>
        {
            var selected = Selected(parse.GetValue(format)); if (selected is null) return 2;
            var result = action(parse.GetValue(repo)!, parse.GetValue(change)!, parse.GetValue(provider));
            Render(result, selected); return result.ExitCode;
        });
        return command;
    }

    private static Command Status(TrackerService service)
    {
        var command = new Command("status", "Show configured providers, durable mappings, and unresolved conflicts.");
        var repo = Repo(); var format = Format(); command.Options.Add(repo); command.Options.Add(format);
        command.SetAction(parse =>
        {
            var selected = Selected(parse.GetValue(format)); if (selected is null) return 2;
            var result = service.Status(parse.GetValue(repo)!); Render(result, selected); return result.ExitCode;
        });
        return command;
    }

    private static Command Resolve(TrackerService service)
    {
        var command = new Command("resolve", "Record a human conflict decision without granting a tracker approval or completion authority.");
        var change = new Argument<string>("change-id"); var task = new Argument<string>("task-id");
        var provider = new Option<string>("--provider") { Required = true, Description = "Configured provider key." };
        var use = new Option<string>("--use") { Required = true, Description = "Resolution: cis, remote, or unlink." };
        var reviewer = new Option<string>("--reviewer") { Required = true, Description = "Human reviewer identity." };
        var reason = new Option<string>("--reason") { Required = true, Description = "Evidence-based resolution rationale." };
        var repo = Repo(); var format = Format();
        command.Arguments.Add(change); command.Arguments.Add(task); command.Options.Add(provider); command.Options.Add(use);
        command.Options.Add(reviewer); command.Options.Add(reason); command.Options.Add(repo); command.Options.Add(format);
        command.SetAction(parse =>
        {
            var selected = Selected(parse.GetValue(format)); if (selected is null) return 2;
            var result = service.Resolve(parse.GetValue(repo)!, parse.GetValue(change)!, parse.GetValue(task)!,
                parse.GetValue(provider)!, parse.GetValue(use)!, parse.GetValue(reviewer)!, parse.GetValue(reason)!);
            Render(result, selected); return result.ExitCode;
        });
        return command;
    }

    private static Option<string> Repo() => new("--repo") { DefaultValueFactory = _ => Directory.GetCurrentDirectory(), Description = "Initialized repository path." };
    private static Option<string> Format() => new("--format") { DefaultValueFactory = _ => "human", Description = "Output: human, json, or agent." };
    private static string? Selected(string? value) => (value ?? "human").ToLowerInvariant() is var selected && selected is "human" or "json" or "agent" ? selected : null;

    private static void Render(TrackerSyncResult result, string format)
    {
        if (format == "json") { Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions)); return; }
        if (format == "agent")
        {
            Console.WriteLine($"status={result.Status};exitCode={result.ExitCode};changeId={result.ChangeId};items={result.Items.Count};conflicts={result.Conflicts.Count(item => item.Status == "open")};applied={result.Applied.ToString().ToLowerInvariant()}");
            foreach (var item in result.Items) Console.WriteLine($"item={Clean(item.Provider)}:{Clean(item.TaskId)};action={item.Action};remoteId={Clean(item.RemoteId)};url={Clean(item.Url)};applied={item.Applied.ToString().ToLowerInvariant()};message={Clean(item.Message)}");
            foreach (var conflict in result.Conflicts.Where(item => item.Status == "open")) Console.WriteLine($"conflict={conflict.Id};provider={Clean(conflict.Provider)};task={Clean(conflict.TaskId)};kind={conflict.Kind};message={Clean(conflict.Message)}");
            foreach (var error in result.Errors) Console.WriteLine($"error={Clean(error)}");
            return;
        }
        Console.WriteLine($"Tracker synchronization: {result.Status}; items: {result.Items.Count}; open conflicts: {result.Conflicts.Count(item => item.Status == "open")}");
        foreach (var item in result.Items) Console.WriteLine($"- {item.Provider}/{item.TaskId}: {item.Action} - {item.Message}");
        foreach (var error in result.Errors) Console.WriteLine($"ERROR: {error}");
    }

    private static void Render(TrackerStatusResult result, string format)
    {
        if (format == "json") { Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions)); return; }
        if (format == "agent")
        {
            Console.WriteLine($"status={result.Status};exitCode={result.ExitCode};providers={result.Providers.Count};mappings={result.Mappings.Count};openConflicts={result.Conflicts.Count(item => item.Status == "open")}");
            foreach (var provider in result.Providers) Console.WriteLine($"provider={Clean(provider.Key)};kind={Clean(provider.Kind)};enabled={provider.Enabled.ToString().ToLowerInvariant()};target={Clean(provider.Target)};direction={Clean(provider.Direction)}");
            foreach (var error in result.Errors) Console.WriteLine($"error={Clean(error)}");
            return;
        }
        Console.WriteLine($"Tracker state: {result.Status}; providers: {result.Providers.Count}; mappings: {result.Mappings.Count}; open conflicts: {result.Conflicts.Count(item => item.Status == "open")}");
    }

    private static string Clean(string? value) => (value ?? "none").Replace(';', ',').Replace('\r', ' ').Replace('\n', ' ').Trim();
}

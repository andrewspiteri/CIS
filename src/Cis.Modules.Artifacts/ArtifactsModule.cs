using System.CommandLine;
using System.Text.Json;
using Cis.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Cis.Modules.Artifacts;

public sealed class ArtifactsModule : ICisModule
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public string Name => "artifacts";
    public string Description => "Inventory, retain, compact, retrieve, restore, and clean derived local artifacts.";

    public void RegisterServices(IServiceCollection services)
    {
        // Planning's doctor inventory is also needed while AgentService is composed.
        // Resolve the shared authority only when an artifact operation actually uses it.
        services.AddSingleton(provider => new ArtifactRetentionService(provider.GetRequiredService<ICisRepositoryContextResolver>(),
            nativeWorkFactory: () => provider.GetService<ICisNativeWorkController>(),
            ownedStorageFactory: () => provider.GetService<ICisOwnedStorageController>()));
        services.AddSingleton<ICisRepositoryDoctorCheck, ArtifactDoctorCheck>();
    }

    public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services)
    {
        var service = services.GetRequiredService<ArtifactRetentionService>();
        var root = new Command(Name, Description);
        root.Subcommands.Add(Read("inventory", "Inventory derived artifact families and retention candidates.", service.Inventory));
        root.Subcommands.Add(Read("plan", "Preview retention actions without changing local artifacts.", service.Plan));
        root.Subcommands.Add(Confirmed("compact", "Archive and verify eligible entries before removing their originals.", service.Compact));
        root.Subcommands.Add(Confirmed("clean", "Delete eligible entries and retain a hash tombstone record.", service.Clean));
        root.Subcommands.Add(Archives(service));
        root.Subcommands.Add(Retrieve(service));
        root.Subcommands.Add(Restore(service));
        commands.Add(root);
    }

    private static Command Read(string name, string description, Func<string, string?, CisNativeWorkOptions?, ArtifactOperationResult> action)
    {
        var command = new Command(name, description); var repo = Repo(); var family = new Option<string?>("--family"); var format = Format();
        command.Options.Add(repo); command.Options.Add(family); command.Options.Add(format); var work = AddWorkOptions(command);
        command.SetAction(parse => Render(action(parse.GetValue(repo)!, parse.GetValue(family), work.Read(option => parse.GetValue(option))), parse.GetValue(format)!));
        return command;
    }

    private static Command Confirmed(string name, string description, Func<string, string, bool, CisNativeWorkOptions?, ArtifactOperationResult> action)
    {
        var command = new Command(name, description); var repo = Repo(); var family = new Option<string>("--family") { Required = true };
        var yes = new Option<bool>("--yes"); var format = Format();
        command.Options.Add(repo); command.Options.Add(family); command.Options.Add(yes); command.Options.Add(format); var work = AddWorkOptions(command);
        command.SetAction(parse => Render(action(parse.GetValue(repo)!, parse.GetValue(family)!, parse.GetValue(yes), work.Read(option => parse.GetValue(option))), parse.GetValue(format)!));
        return command;
    }

    private static Command Archives(ArtifactRetentionService service)
    {
        var command = new Command("archives", "List verified reversible archives."); var repo = Repo(); var format = Format();
        command.Options.Add(repo); command.Options.Add(format); var work = AddWorkOptions(command);
        command.SetAction(parse => Render(service.Archives(parse.GetValue(repo)!, work.Read(option => parse.GetValue(option))), parse.GetValue(format)!)); return command;
    }

    private static Command Retrieve(ArtifactRetentionService service)
    {
        var command = new Command("retrieve", "Extract an archive or one retained entry into a non-authoritative retrieval area.");
        var archive = new Option<string>("--archive") { Required = true }; var entry = new Option<string?>("--entry"); var repo = Repo(); var format = Format();
        command.Options.Add(archive); command.Options.Add(entry); command.Options.Add(repo); command.Options.Add(format); var work = AddWorkOptions(command);
        command.SetAction(parse => Render(service.Retrieve(parse.GetValue(repo)!, parse.GetValue(archive)!, parse.GetValue(entry), work.Read(option => parse.GetValue(option))), parse.GetValue(format)!));
        return command;
    }

    private static Command Restore(ArtifactRetentionService service)
    {
        var command = new Command("restore", "Restore a verified archive to original local paths without overwriting conflicts.");
        var archive = new Option<string>("--archive") { Required = true }; var yes = new Option<bool>("--yes"); var repo = Repo(); var format = Format();
        command.Options.Add(archive); command.Options.Add(yes); command.Options.Add(repo); command.Options.Add(format); var work = AddWorkOptions(command);
        command.SetAction(parse => Render(service.Restore(parse.GetValue(repo)!, parse.GetValue(archive)!, parse.GetValue(yes), work.Read(option => parse.GetValue(option))), parse.GetValue(format)!));
        return command;
    }

    private static int Render(ArtifactOperationResult result, string format)
    {
        var selected = format.Trim().ToLowerInvariant();
        if (selected == "json") Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        else if (selected == "agent")
        {
            Console.WriteLine($"status={Clean(result.Status)};exitCode={result.ExitCode};applied={result.Applied.ToString().ToLowerInvariant()};rules={result.Rules.Count};entries={result.Entries.Count};candidates={result.Entries.Count(item => item.Candidate)};archives={result.Archives.Count};outputs={result.Outputs.Count}");
            if (result.Storage is { } storage) Console.WriteLine($"configuredLiveBytes={storage.ConfiguredLiveBytes};archiveBytes={storage.ArchiveBytes};managementBytes={storage.ManagementBytes};totalReportedBytes={storage.TotalReportedBytes}");
            if (result.OwnedStorage is { } owned)
            {
                Console.WriteLine($"ownedStorageStatus={owned.Status};ownedStorageRequired={owned.Required.ToString().ToLowerInvariant()}");
                if (owned.Storage is { } usage) Console.WriteLine($"ownedBytes={usage.OwnedBytes};protectedOwnedBytes={usage.ProtectedBytes};reservedOwnedBytes={usage.ReservedBytes};remainingOwnedBytes={usage.RemainingBytes};maximumOwnedBytes={usage.MaximumBytes};restorationBytes={usage.RestorationBytes};storageOwner={usage.AuthorityRepositoryId}/{usage.ChangeId}/{usage.OwnerTaskId};ceilingReference={Clean(usage.CeilingReference)}");
            }
            if (result.Work?.Work is { } accounting) Console.WriteLine($"workOwner={accounting.AuthorityRepositoryId}/{accounting.ChangeId}/{accounting.OwnerTaskId};consumedMilliseconds={accounting.Task?.ConsumedMilliseconds};reservedMilliseconds={accounting.Task?.ReservedMilliseconds};remainingMilliseconds={accounting.Task?.RemainingMilliseconds};usedAttempts={accounting.Task?.UsedAttempts}");
            foreach (var usage in result.FamilyStorage) Console.WriteLine($"family={Clean(usage.Family)};liveBytes={usage.LiveBytes};protectedBytes={usage.ProtectedBytes};candidateBytes={usage.CandidateBytes};plannedLiveBytes={usage.PlannedLiveBytes};maximumBytes={usage.MaximumBytes};budgetExceeded={usage.BudgetExceeded.ToString().ToLowerInvariant()};budgetBlocked={usage.BudgetBlocked.ToString().ToLowerInvariant()}");
            foreach (var entry in result.Entries) Console.WriteLine($"entry={Clean(entry.Path)};family={Clean(entry.Family)};kind={entry.Kind};bytes={entry.SizeBytes};candidate={entry.Candidate.ToString().ToLowerInvariant()};protected={entry.Protected.ToString().ToLowerInvariant()};action={entry.Action};digest={entry.Digest}");
            foreach (var archive in result.Archives) Console.WriteLine($"archive={archive.ArchiveId};family={Clean(archive.Family)};entries={archive.Entries.Count};digest={archive.ZipDigest};path={Clean(archive.ZipPath)}");
            foreach (var output in result.Outputs) Console.WriteLine($"output={Clean(output)}");
            foreach (var error in result.Errors) Console.WriteLine($"error={Clean(error)}");
        }
        else if (selected == "human")
        {
            Console.WriteLine($"Local artifacts: {result.Status}; entries={result.Entries.Count}; candidates={result.Entries.Count(item => item.Candidate)}; archives={result.Archives.Count}");
            if (result.Storage is { } storage) Console.WriteLine($"Configured live entries: {storage.ConfiguredLiveBytes} bytes; artifact management: {storage.ManagementBytes} bytes (including {storage.ArchiveBytes} archive bytes); total reported: {storage.TotalReportedBytes} bytes.");
            if (result.OwnedStorage is { } owned)
                Console.WriteLine(owned.Storage is { } ownedUsage
                    ? $"Owner storage: {ownedUsage.OwnedBytes} physical bytes, {ownedUsage.ProtectedBytes} protected, {ownedUsage.ReservedBytes} reserved, {ownedUsage.RemainingBytes} remaining; ceiling {ownedUsage.MaximumBytes}, restoration reserve {ownedUsage.RestorationBytes}. {ownedUsage.Coverage}"
                    : $"Owner storage: {owned.Status}; {(owned.Required ? "resolve the canonical owner and finite allocation" : "unaccounted diagnostic coverage")}.");
            foreach (var usage in result.FamilyStorage.Where(usage => usage.MaximumBytes is not null))
                Console.WriteLine($"- {usage.Family}: {usage.LiveBytes} live bytes; {usage.CandidateBytes} eligible bytes; {usage.PlannedLiveBytes} bytes after the plan; budget {usage.MaximumBytes} bytes{(usage.BudgetBlocked ? "; BLOCKED by protected entries" : "")}.");
            foreach (var entry in result.Entries) Console.WriteLine($"- {entry.Family}: {entry.Path}; {entry.SizeBytes} bytes; {entry.Action}; {entry.Digest[..12]}");
            foreach (var error in result.Errors) Console.WriteLine($"ERROR: {error}");
        }
        else { Console.Error.WriteLine("Unsupported format. Use human, json, or agent."); return 2; }
        return result.ExitCode;
    }

    private static Option<string> Repo() => new("--repo") { DefaultValueFactory = _ => Directory.GetCurrentDirectory() };
    private static Option<string> Format() => new("--format") { DefaultValueFactory = _ => "human" };
    private static string Clean(string value) => value.Replace(';', ',').Replace('\r', ' ').Replace('\n', ' ').Trim();

    private sealed record WorkOptions(Option<string?> Change, Option<string?> Task, Option<string?> Parent,
        Option<string?> Authority, Option<string?> Actor)
    {
        public CisNativeWorkOptions? Read(Func<Option<string?>, string?> read)
        {
            var result = new CisNativeWorkOptions(read(Change), read(Task), read(Parent), read(Authority), read(Actor));
            return result == new CisNativeWorkOptions() ? null : result;
        }
    }
    private static WorkOptions AddWorkOptions(Command command)
    {
        var options = new WorkOptions(new("--change") { Description = "Existing approved containing change; no new allocation." },
            new("--task") { Description = "Existing canonical task or inherited accounting owner." },
            new("--parent-execution") { Description = "Existing admitted parent; also inherited from verified CIS context." },
            new("--authority") { Description = "Owning authority repository for an associated owned target." },
            new("--actor") { Description = "Authorized controller; cleanup also requires its existing delegation." });
        foreach (var option in new[] { options.Change, options.Task, options.Parent, options.Authority, options.Actor }) command.Options.Add(option);
        return options;
    }
}

using System.CommandLine;
using System.Text.Json;
using Cis.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Cis.Modules.Verify;

public sealed class VerifyModule : ICisModule
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public string Name => "verify";
    public string Description => "Compare planned and actual delivery, record evidence, validate gates, and capture human acceptance.";

    public void RegisterServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<VerifyService>();
    }

    public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(services);
        var service = services.GetRequiredService<VerifyService>();
        var root = new Command(Name, Description);
        root.Subcommands.Add(Change("diff", "Capture changed files from every workspace repository baseline.", service.Diff));
        root.Subcommands.Add(Change("compare", "Compare planned repository and path targets with observed changes.", service.Compare));
        root.Subcommands.Add(Change("validate", "Validate delivery gates, current snapshot, and evidence.", service.Validate));
        root.Subcommands.Add(Evidence(service));
        root.Subcommands.Add(Authority("accept", "Record explicit human final verification acceptance.", service.Accept));
        root.Subcommands.Add(Authority("finalize", "Complete lifecycle tasks, close the dossier, recapture, and accept atomically.", service.Finalize));
        commands.Add(root);
    }

    private static Command Change(string name, string description, Func<string, string, VerifyResult> action)
    {
        var command = new Command(name, description);
        var id = new Argument<string>("change-id");
        var repo = Repo();
        var format = Format();
        command.Arguments.Add(id);
        command.Options.Add(repo);
        command.Options.Add(format);
        command.SetAction(parse => Render(action(parse.GetValue(repo)!, parse.GetValue(id)!), parse.GetValue(format)!));
        return command;
    }

    private static Command Evidence(VerifyService service)
    {
        var command = new Command("evidence", "Append one exact verification evidence row.");
        var id = new Argument<string>("change-id");
        var task = Required("--task");
        var check = Required("--check");
        var artifact = Required("--artifact");
        var result = Required("--result");
        var notes = new Option<string>("--notes") { DefaultValueFactory = _ => "" };
        var repo = Repo();
        var format = Format();
        command.Arguments.Add(id);
        foreach (var option in new Option[] { task, check, artifact, result, notes, repo, format }) command.Options.Add(option);
        command.SetAction(parse => Render(service.Evidence(
            parse.GetValue(repo)!, parse.GetValue(id)!, parse.GetValue(task)!, parse.GetValue(check)!,
            parse.GetValue(artifact)!, parse.GetValue(result)!, parse.GetValue(notes)!), parse.GetValue(format)!));
        return command;
    }

    private static Command Authority(string name, string description, Func<string, string, string, string, VerifyResult> action)
    {
        var command = new Command(name, description);
        var id = new Argument<string>("change-id");
        var reviewer = Required("--reviewer");
        var reason = Required("--reason");
        var repo = Repo();
        var format = Format();
        command.Arguments.Add(id);
        foreach (var option in new Option[] { reviewer, reason, repo, format }) command.Options.Add(option);
        command.SetAction(parse => Render(action(
            parse.GetValue(repo)!, parse.GetValue(id)!, parse.GetValue(reviewer)!, parse.GetValue(reason)!),
            parse.GetValue(format)!));
        return command;
    }

    private static Option<string> Required(string name) => new(name) { Required = true };
    private static Option<string> Repo() => new("--repo") { DefaultValueFactory = _ => Directory.GetCurrentDirectory() };
    private static Option<string> Format() => new("--format") { DefaultValueFactory = _ => "human" };

    private static int Render(VerifyResult result, string format)
    {
        if (format == "json")
        {
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        }
        else if (format == "agent")
        {
            var applied = result.Applied ? "true" : "false";
            Console.WriteLine($"status={result.Status};exitCode={result.ExitCode};changeId={result.ChangeId};files={result.Snapshot?.Files.Count ?? 0};repositories={result.Snapshot?.Repositories?.Count ?? 0};findings={result.Findings.Count};applied={applied}");
            foreach (var finding in result.Findings)
                Console.WriteLine($"finding={finding.Severity}:{finding.Code};message={finding.Message.Replace(';', ',')};path={finding.Path ?? "none"}");
        }
        else if (format == "human")
        {
            Console.WriteLine($"Verification: {result.Status}; files={result.Snapshot?.Files.Count ?? 0}; repositories={result.Snapshot?.Repositories?.Count ?? 0}; findings={result.Findings.Count}");
            foreach (var finding in result.Findings)
                Console.WriteLine($"{finding.Severity.ToUpperInvariant()} {finding.Code}: {finding.Message}");
        }
        else
        {
            return 2;
        }

        return result.ExitCode;
    }
}

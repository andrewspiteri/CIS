using System.CommandLine;
using System.Text.Json;
using Cis.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Cis.Modules.Change;

public sealed class ChangeModule : ICisModule
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public string Name => "change";

    public string Description => "Create and manage repository-owned change dossiers.";

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<ChangeDossierStore>();
    }

    public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services)
    {
        var store = services.GetRequiredService<ChangeDossierStore>();
        var command = new Command(Name, Description);
        command.Subcommands.Add(CreateCreateCommand(store));
        command.Subcommands.Add(CreateListCommand(store));
        command.Subcommands.Add(CreateShowCommand(store));
        command.Subcommands.Add(CreateStatusCommand(store));
        command.Subcommands.Add(CreateRebaselineCommand(store));
        command.Subcommands.Add(CreateAuthorizeCommand(store));
        command.Subcommands.Add(CreateCloseCommand(store));
        commands.Add(command);
    }

    private static Command CreateCreateCommand(ChangeDossierStore store)
    {
        var command = new Command("create", "Create a change dossier against the current exact graph or Git baseline.");
        var title = new Option<string>("--title") { Required = true, Description = "Short change title." };
        var outcome = new Option<string>("--outcome") { Required = true, Description = "Observable outcome the change must deliver." };
        var requestedId = new Option<string?>("--id") { Description = "Optional explicit ID matching CIS-0001." };
        var roots = new Option<string[]>("--root")
        {
            Description = "Impact root as <node-id>[#<kind>]. Repeat for multiple roots.",
            Arity = ArgumentArity.ZeroOrMore,
            AllowMultipleArgumentsPerToken = true,
        };
        var repo = RepositoryOption();
        var format = FormatOption();
        command.Options.Add(title);
        command.Options.Add(outcome);
        command.Options.Add(requestedId);
        command.Options.Add(roots);
        command.Options.Add(repo);
        command.Options.Add(format);
        command.SetAction(parseResult =>
        {
            var parsedRoots = (parseResult.GetValue(roots) ?? [])
                .Select(ParseRoot)
                .ToArray();
            var result = store.Create(new ChangeCreateRequest(
                parseResult.GetValue(repo) ?? Directory.GetCurrentDirectory(),
                parseResult.GetValue(title) ?? string.Empty,
                parseResult.GetValue(outcome) ?? string.Empty,
                parsedRoots,
                parseResult.GetValue(requestedId)));
            Render(result, Format(parseResult.GetValue(format)));
            return result.ExitCode;
        });
        return command;
    }

    private static Command CreateAuthorizeCommand(ChangeDossierStore store)
    {
        var command = new Command("authorize", "Record one human authorization of an outcome, explicit scope, overall limits and task delegation.");
        var id = new Argument<string>("change-id");
        var input = new Option<string>("--input") { Required = true, Description = "Repository-relative JSON outcome, scope, limits and delegation proposal." };
        var reviewer = new Option<string>("--reviewer") { Required = true, Description = "Actual human authority for this decision." };
        var reason = new Option<string>("--reason") { Required = true, Description = "Reason for this exact authorization or material amendment." };
        var repo = RepositoryOption();
        var format = FormatOption();
        command.Arguments.Add(id);
        foreach (var option in new Option[] { input, reviewer, reason, repo, format }) command.Options.Add(option);
        command.SetAction(parse =>
        {
            ChangeResult result;
            try
            {
                var repository = Path.GetFullPath(parse.GetValue(repo) ?? Directory.GetCurrentDirectory());
                if (!CisPathSafety.TryResolveUnderRoot(repository, parse.GetValue(input) ?? string.Empty, out var path)
                    || CisPathSafety.ContainsReparsePoint(repository, path))
                    throw new InvalidDataException("The authorization proposal must be a bounded unlinked file in this repository.");
                result = store.Authorize(new(repository, parse.GetValue(id) ?? string.Empty,
                    CisOutcomeAuthority.ReadRequest(path), parse.GetValue(reviewer) ?? string.Empty, parse.GetValue(reason) ?? string.Empty));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
            { result = new("invalid", null, [], [error.Message], false); }
            Render(result, Format(parse.GetValue(format)));
            return result.ExitCode;
        });
        return command;
    }

    private static Command CreateListCommand(ChangeDossierStore store)
    {
        var command = new Command("list", "List change dossiers.");
        var repo = RepositoryOption();
        var format = FormatOption();
        command.Options.Add(repo);
        command.Options.Add(format);
        command.SetAction(parseResult =>
        {
            var result = store.List(parseResult.GetValue(repo) ?? Directory.GetCurrentDirectory());
            Render(result, Format(parseResult.GetValue(format)));
            return result.ExitCode;
        });
        return command;
    }

    private static Command CreateShowCommand(ChangeDossierStore store)
        => CreateReadCommand("show", "Show a change dossier.", store);

    private static Command CreateStatusCommand(ChangeDossierStore store)
        => CreateReadCommand("status", "Show a change dossier lifecycle status.", store);

    private static Command CreateReadCommand(string name, string description, ChangeDossierStore store)
    {
        var command = new Command(name, description);
        var id = new Argument<string>("change-id");
        var repo = RepositoryOption();
        var format = FormatOption();
        command.Arguments.Add(id);
        command.Options.Add(repo);
        command.Options.Add(format);
        command.SetAction(parseResult =>
        {
            var changeId = parseResult.GetValue(id) ?? string.Empty;
            var repository = parseResult.GetValue(repo) ?? Directory.GetCurrentDirectory();
            var change = store.Read(repository, changeId);
            ChangeResult result = change is null
                ? new ChangeResult("not-found", null, [], [$"Change dossier was not found: {changeId}"], false)
                : new ChangeResult("found", change, [change], [], false);
            if (change is not null)
            {
                try { result = result with { Authorization = store.ReadAuthorization(repository, change.Id) }; }
                catch (InvalidDataException error) { result = result with { Status = "invalid", Errors = [error.Message] }; }
            }
            Render(result, Format(parseResult.GetValue(format)));
            return result.ExitCode;
        });
        return command;
    }

    private static Command CreateCloseCommand(ChangeDossierStore store)
    {
        var command = new Command("close", "Record explicit human closure of a change dossier.");
        var id = new Argument<string>("change-id");
        var repo = RepositoryOption();
        var format = FormatOption();
        command.Arguments.Add(id);
        command.Options.Add(repo);
        command.Options.Add(format);
        command.SetAction(parseResult =>
        {
            var result = store.Close(
                parseResult.GetValue(repo) ?? Directory.GetCurrentDirectory(),
                parseResult.GetValue(id) ?? string.Empty);
            Render(result, Format(parseResult.GetValue(format)));
            return result.ExitCode;
        });
        return command;
    }

    private static Command CreateRebaselineCommand(ChangeDossierStore store)
    {
        var command = new Command("rebaseline", "Adopt the current exact graph/Git baseline before impact findings or work items exist, preserving an audit event.");
        var id = new Argument<string>("change-id");
        var actor = new Option<string>("--actor") { Required = true, Description = "Human or agent identity authorizing the recorded baseline change." };
        var reason = new Option<string>("--reason") { Required = true, Description = "Auditable reason for adopting the current baseline." };
        var repo = RepositoryOption();
        var format = FormatOption();
        command.Arguments.Add(id);
        command.Options.Add(actor);
        command.Options.Add(reason);
        command.Options.Add(repo);
        command.Options.Add(format);
        command.SetAction(parseResult =>
        {
            var result = store.Rebaseline(new ChangeRebaselineRequest(
                parseResult.GetValue(repo) ?? Directory.GetCurrentDirectory(),
                parseResult.GetValue(id) ?? string.Empty,
                parseResult.GetValue(actor) ?? string.Empty,
                parseResult.GetValue(reason) ?? string.Empty));
            Render(result, Format(parseResult.GetValue(format)));
            return result.ExitCode;
        });
        return command;
    }

    private static ChangeRoot ParseRoot(string value)
    {
        var separator = value.LastIndexOf('#');
        return separator > 0
            ? new ChangeRoot(value[..separator].Trim(), value[(separator + 1)..].Trim())
            : new ChangeRoot(value.Trim(), null);
    }

    private static Option<string> RepositoryOption() => new("--repo")
    {
        Description = "Repository path. Defaults to the current directory.",
        DefaultValueFactory = _ => Directory.GetCurrentDirectory(),
    };

    private static Option<string> FormatOption() => new("--format")
    {
        Description = "Output format: human, json, or agent.",
        DefaultValueFactory = _ => "human",
    };

    private static string Format(string? value)
        => value is "json" or "agent" ? value : "human";

    private static void Render(ChangeResult result, string format)
    {
        if (format == "json")
        {
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return;
        }

        if (format == "agent")
        {
            Console.WriteLine($"status={result.Status};exitCode={result.ExitCode};applied={result.Applied.ToString().ToLowerInvariant()}");
            foreach (var change in result.Changes)
            {
                Console.WriteLine($"change={change.Id};status={change.Status};baseline={change.Baseline};path={change.RelativePath};title={OneLine(change.Title)}");
            }
            if (result.Authorization is { } authorization)
                Console.WriteLine($"authorization={OneLine(authorization.Id)};decision={OneLine(authorization.Request.DecisionReference)};reviewer={OneLine(authorization.Reviewer)};digest={authorization.Digest};visualUiDerived={authorization.VisualUiDerivedFromAcceptedSource.ToString().ToLowerInvariant()}");

            foreach (var error in result.Errors)
            {
                Console.WriteLine($"error={OneLine(error)}");
            }

            return;
        }

        Console.WriteLine($"Status: {result.Status}");
        foreach (var change in result.Changes)
        {
            Console.WriteLine($"{change.Id} [{change.Status}] {change.Title}");
            Console.WriteLine($"  Baseline: {change.BaselineKind}:{change.Baseline}");
            Console.WriteLine($"  Path: {change.RelativePath}");
        }
        if (result.Authorization is { } authority)
        {
            Console.WriteLine($"Outcome authorization: {authority.Id}");
            Console.WriteLine($"  Human decision: {authority.Request.DecisionReference}; reviewer: {authority.Reviewer}");
            Console.WriteLine($"  Overall limits: {authority.Request.Seconds} seconds; {authority.Request.Attempts} attempts; {authority.Request.Storage.MaximumBytes} owned bytes.");
            if (authority.VisualUiDerivedFromAcceptedSource) Console.WriteLine("  Visual UI applicability comes from accepted source direction, without a separate human answer.");
        }

        foreach (var error in result.Errors)
        {
            Console.WriteLine($"ERROR: {error}");
        }
    }

    private static string OneLine(string value)
        => value.Replace('\r', ' ').Replace('\n', ' ').Replace(';', ',').Trim();
}

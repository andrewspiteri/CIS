using System.CommandLine;
using System.Text.Json;
using Cis.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Cis.Modules.Plan;

public sealed class PlanModule : ICisModule
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public string Name => "plan";

    public string Description => "Build, import, validate, and approve bounded work from accepted impacts and feature specifications.";

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<ICisTaskTypeProvider, CoreTaskTypeProvider>();
        services.AddSingleton<TaskTypeRegistry>();
        services.AddSingleton<TaskTypeCapabilityStore>();
        services.AddSingleton<PlanningService>();
    }

    public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services)
    {
        var service = services.GetRequiredService<PlanningService>();
        var plan = new Command(Name, Description);
        plan.Subcommands.Add(Create("build", "Build bounded work from accepted impact findings.", service.Build));
        plan.Subcommands.Add(CreateImportSpecCommand(service));
        plan.Subcommands.Add(Create("show", "Show the current durable plan.", service.Show));
        plan.Subcommands.Add(Create("validate", "Validate impact coverage, dependencies, decisions, acceptance, and validation.", service.Validate));
        plan.Subcommands.Add(CreateApproveCommand(service));
        plan.Subcommands.Add(Create("status", "Show plan state and validation readiness.", service.Status));
        plan.Subcommands.Add(CreateCapabilityCommand(service));
        plan.Subcommands.Add(CreateTaskCommand(service));
        commands.Add(plan);
    }

    private static Command CreateTaskCommand(PlanningService service)
    {
        var task = new Command("task", "Transition an individual durable task through the governed lifecycle.");
        var transition = new Command("transition", "Record a task lifecycle transition with actor and reason.");
        var changeId = new Argument<string>("change-id");
        var taskId = new Argument<string>("task-id");
        var status = new Option<string>("--status") { Required = true, Description = "Target status." };
        var actor = new Option<string>("--actor") { Required = true, Description = "Person or agent performing the transition." };
        var reason = new Option<string>("--reason") { Required = true, Description = "Evidence-based transition reason." };
        var repo = new Option<string>("--repo") { DefaultValueFactory = _ => Directory.GetCurrentDirectory(), Description = "Repository path." };
        var format = new Option<string>("--format") { DefaultValueFactory = _ => "human", Description = "Output format: human, json, or agent." };
        transition.Arguments.Add(changeId);
        transition.Arguments.Add(taskId);
        transition.Options.Add(status);
        transition.Options.Add(actor);
        transition.Options.Add(reason);
        transition.Options.Add(repo);
        transition.Options.Add(format);
        transition.SetAction(parseResult =>
        {
            var result = service.TransitionTask(new PlanTaskTransitionRequest(
                parseResult.GetValue(repo) ?? Directory.GetCurrentDirectory(),
                parseResult.GetValue(changeId) ?? string.Empty,
                parseResult.GetValue(taskId) ?? string.Empty,
                parseResult.GetValue(status) ?? string.Empty,
                parseResult.GetValue(actor) ?? string.Empty,
                parseResult.GetValue(reason) ?? string.Empty));
            Render(result, parseResult.GetValue(format) is "json" or "agent" ? parseResult.GetValue(format)! : "human");
            return result.ExitCode;
        });
        task.Subcommands.Add(transition);
        task.Subcommands.Add(CreateTaskMigrationCommand(service));
        return task;
    }

    private static Command CreateTaskMigrationCommand(PlanningService service)
    {
        var command = new Command("migrate-type", "Explicitly migrate an extension task instance to the repository-selected compatible type while preserving human-managed evidence.");
        var changeId = new Argument<string>("change-id");
        var taskId = new Argument<string>("task-id");
        var target = new Option<string>("--to") { Required = true, Description = "Selected replacement task-type key." };
        var reviewer = new Option<string>("--reviewer") { Required = true, Description = "Human reviewer authorizing the migration." };
        var reason = new Option<string>("--reason") { Required = true, Description = "Migration rationale." };
        var repo = new Option<string>("--repo") { DefaultValueFactory = _ => Directory.GetCurrentDirectory(), Description = "Repository path." };
        var format = new Option<string>("--format") { DefaultValueFactory = _ => "human", Description = "Output format: human, json, or agent." };
        command.Arguments.Add(changeId);
        command.Arguments.Add(taskId);
        command.Options.Add(target);
        command.Options.Add(reviewer);
        command.Options.Add(reason);
        command.Options.Add(repo);
        command.Options.Add(format);
        command.SetAction(parseResult =>
        {
            var result = service.MigrateTaskType(new TaskTypeMigrationRequest(
                parseResult.GetValue(repo) ?? Directory.GetCurrentDirectory(),
                parseResult.GetValue(changeId) ?? string.Empty,
                parseResult.GetValue(taskId) ?? string.Empty,
                parseResult.GetValue(target) ?? string.Empty,
                parseResult.GetValue(reviewer) ?? string.Empty,
                parseResult.GetValue(reason) ?? string.Empty));
            Render(result, parseResult.GetValue(format) is "json" or "agent" ? parseResult.GetValue(format)! : "human");
            return result.ExitCode;
        });
        return command;
    }

    private static Command CreateCapabilityCommand(PlanningService service)
    {
        var capability = new Command("capability", "Inspect and govern repository task-type capability selections.");
        var status = new Command("status", "Show selections and unresolved registered-provider conflicts.");
        var statusRepo = new Option<string>("--repo") { DefaultValueFactory = _ => Directory.GetCurrentDirectory(), Description = "Repository path." };
        var statusFormat = new Option<string>("--format") { DefaultValueFactory = _ => "human", Description = "Output format: human, json, or agent." };
        status.Options.Add(statusRepo);
        status.Options.Add(statusFormat);
        status.SetAction(parseResult =>
        {
            var result = service.CapabilityStatus(parseResult.GetValue(statusRepo) ?? Directory.GetCurrentDirectory());
            RenderCapability(result, parseResult.GetValue(statusFormat) is "json" or "agent" ? parseResult.GetValue(statusFormat)! : "human");
            return result.ExitCode;
        });

        var select = new Command("select", "Record a human-approved repository capability selection and explicit compatible replacements.");
        var capabilityKey = new Argument<string>("capability-key");
        var type = new Option<string>("--type") { Required = true, Description = "Registered selected task-type key." };
        var replaces = new Option<string[]>("--replaces")
        {
            Arity = ArgumentArity.ZeroOrMore,
            AllowMultipleArgumentsPerToken = true,
            Description = "Extension task-type keys explicitly replaced by this selection.",
        };
        var reviewer = new Option<string>("--reviewer") { Required = true, Description = "Human reviewer identity." };
        var reason = new Option<string>("--reason") { Required = true, Description = "Selection and replacement rationale." };
        var repo = new Option<string>("--repo") { DefaultValueFactory = _ => Directory.GetCurrentDirectory(), Description = "Repository path." };
        var format = new Option<string>("--format") { DefaultValueFactory = _ => "human", Description = "Output format: human, json, or agent." };
        select.Arguments.Add(capabilityKey);
        select.Options.Add(type);
        select.Options.Add(replaces);
        select.Options.Add(reviewer);
        select.Options.Add(reason);
        select.Options.Add(repo);
        select.Options.Add(format);
        select.SetAction(parseResult =>
        {
            var result = service.SelectCapability(new TaskTypeCapabilitySelectRequest(
                parseResult.GetValue(repo) ?? Directory.GetCurrentDirectory(),
                parseResult.GetValue(capabilityKey) ?? string.Empty,
                parseResult.GetValue(type) ?? string.Empty,
                parseResult.GetValue(replaces) ?? [],
                parseResult.GetValue(reviewer) ?? string.Empty,
                parseResult.GetValue(reason) ?? string.Empty));
            RenderCapability(result, parseResult.GetValue(format) is "json" or "agent" ? parseResult.GetValue(format)! : "human");
            return result.ExitCode;
        });
        capability.Subcommands.Add(status);
        capability.Subcommands.Add(select);
        return capability;
    }

    private static Command CreateApproveCommand(PlanningService service)
    {
        var command = new Command("approve", "Record explicit human approval of a valid plan with reviewer identity and rationale.");
        var id = new Argument<string>("change-id");
        var reviewer = new Option<string>("--reviewer") { Required = true, Description = "Human reviewer identity." };
        var reason = new Option<string>("--reason") { Required = true, Description = "Approval rationale." };
        var repo = new Option<string>("--repo") { DefaultValueFactory = _ => Directory.GetCurrentDirectory(), Description = "Repository path." };
        var format = new Option<string>("--format") { DefaultValueFactory = _ => "human", Description = "Output format: human, json, or agent." };
        command.Arguments.Add(id);
        command.Options.Add(reviewer);
        command.Options.Add(reason);
        command.Options.Add(repo);
        command.Options.Add(format);
        command.SetAction(parseResult =>
        {
            var result = service.Approve(
                parseResult.GetValue(repo) ?? Directory.GetCurrentDirectory(),
                parseResult.GetValue(id) ?? string.Empty,
                parseResult.GetValue(reviewer) ?? string.Empty,
                parseResult.GetValue(reason) ?? string.Empty);
            Render(result, parseResult.GetValue(format) is "json" or "agent" ? parseResult.GetValue(format)! : "human");
            return result.ExitCode;
        });
        return command;
    }

    private static Command CreateImportSpecCommand(PlanningService service)
    {
        var command = new Command("import-spec", "Import a feature specification and create a complexity-bounded task plan.");
        var id = new Argument<string>("change-id");
        var file = new Option<string>("--file")
        {
            Required = true,
            Description = "Repository-relative Markdown feature specification path.",
        };
        var repo = new Option<string>("--repo")
        {
            DefaultValueFactory = _ => Directory.GetCurrentDirectory(),
            Description = "Repository path.",
        };
        var format = new Option<string>("--format")
        {
            DefaultValueFactory = _ => "human",
            Description = "Output format: human, json, or agent.",
        };
        command.Arguments.Add(id);
        command.Options.Add(file);
        command.Options.Add(repo);
        command.Options.Add(format);
        command.SetAction(parseResult =>
        {
            var result = service.ImportSpec(new FeatureSpecImportRequest(
                parseResult.GetValue(repo) ?? Directory.GetCurrentDirectory(),
                parseResult.GetValue(id) ?? string.Empty,
                parseResult.GetValue(file) ?? string.Empty));
            Render(result, parseResult.GetValue(format) is "json" or "agent"
                ? parseResult.GetValue(format)!
                : "human");
            return result.ExitCode;
        });
        return command;
    }

    private static Command Create(
        string name,
        string description,
        Func<string, string, PlanResult> action)
    {
        var command = new Command(name, description);
        var id = new Argument<string>("change-id");
        var repo = new Option<string>("--repo")
        {
            DefaultValueFactory = _ => Directory.GetCurrentDirectory(),
            Description = "Repository path.",
        };
        var format = new Option<string>("--format")
        {
            DefaultValueFactory = _ => "human",
            Description = "Output format: human, json, or agent.",
        };
        command.Arguments.Add(id);
        command.Options.Add(repo);
        command.Options.Add(format);
        command.SetAction(parseResult =>
        {
            var result = action(
                parseResult.GetValue(repo) ?? Directory.GetCurrentDirectory(),
                parseResult.GetValue(id) ?? string.Empty);
            Render(result, parseResult.GetValue(format) is "json" or "agent" ? parseResult.GetValue(format)! : "human");
            return result.ExitCode;
        });
        return command;
    }

    private static void Render(PlanResult result, string format)
    {
        if (format == "json")
        {
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return;
        }

        if (format == "agent")
        {
            Console.WriteLine($"status={result.Status};changeId={result.ChangeId};planStatus={result.PlanStatus};workItems={result.WorkItems.Count};applied={result.Applied.ToString().ToLowerInvariant()};exitCode={result.ExitCode}");
            if (result.Source is not null)
            {
                Console.WriteLine($"featureSpec={result.Source.Path};sha256={result.Source.Sha256};requirements={result.Source.Requirements};frontend={result.Source.FrontendChanges.ToString().ToLowerInvariant()};targets={string.Join(',', result.Source.Targets)};stack={string.Join(',', result.Source.Stack)}");
            }
            foreach (var item in result.WorkItems)
            {
                Console.WriteLine($"workItem={item.Id};status={item.Status};category={item.Category};complexity={item.Complexity};parent={item.ParentId};taskPath={item.TaskPath};requirements={string.Join(',', item.RequirementIds)};impacts={string.Join(',', item.ImpactIds)};dependsOn={string.Join(',', item.DependsOn)};title={Clean(item.Title)}");
                Console.WriteLine($"acceptance.{item.Id}={Clean(item.AcceptanceCriteria)}");
                Console.WriteLine($"validation.{item.Id}={Clean(item.Validation)}");
            }

            RenderValidation(result.Validation, prefix => Console.WriteLine(prefix));
            foreach (var error in result.Errors)
            {
                Console.WriteLine($"error={Clean(error)}");
            }

            return;
        }

        Console.WriteLine($"Status: {result.Status}; plan: {result.PlanStatus}");
        if (result.Source is not null)
        {
            Console.WriteLine($"Feature spec: {result.Source.Path} ({result.Source.Requirements} requirements; frontend: {result.Source.FrontendChanges})");
        }
        foreach (var item in result.WorkItems)
        {
            Console.WriteLine($"{item.Id} [{item.Complexity}; {item.Status}] {item.Title}" +
                              HumanDependencySuffix(item.DependsOn));
        }

        if (result.Validation is { } validation)
        {
            Console.WriteLine($"Valid: {validation.Valid}; impact coverage: {validation.CoveredImpacts}/{validation.AcceptedImpacts}; open decisions: {validation.OpenDecisions}");
            foreach (var error in validation.Errors)
            {
                Console.WriteLine($"  ERROR: {error}");
            }
        }

        foreach (var error in result.Errors)
        {
            Console.WriteLine($"ERROR: {error}");
        }
    }

    private static void RenderCapability(TaskTypeCapabilityResult result, string format)
    {
        if (format == "json")
        {
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return;
        }
        if (format == "agent")
        {
            Console.WriteLine($"status={result.Status};selections={result.Selections.Count};conflicts={result.Conflicts.Count};applied={result.Applied.ToString().ToLowerInvariant()};exitCode={result.ExitCode}");
            foreach (var selection in result.Selections)
                Console.WriteLine($"selection={Clean(selection.CapabilityKey)};type={Clean(selection.SelectedTypeKey)};replaces={string.Join(',', selection.ReplacesTypeKeys)};reviewer={Clean(selection.Reviewer)};timestamp={selection.TimestampUtc:O};rationale={Clean(selection.Rationale)}");
            foreach (var conflict in result.Conflicts)
                Console.WriteLine($"conflict={Clean(conflict.CapabilityKey)};candidates={string.Join(',', conflict.CandidateTypeKeys)};reason={Clean(conflict.Reason)}");
            foreach (var error in result.Errors) Console.WriteLine($"error={Clean(error)}");
            return;
        }
        Console.WriteLine($"Task-type capabilities: {result.Status}; selections: {result.Selections.Count}; conflicts: {result.Conflicts.Count}");
        foreach (var selection in result.Selections)
            Console.WriteLine($"  {selection.CapabilityKey}: {selection.SelectedTypeKey} (replaces: {(selection.ReplacesTypeKeys.Count == 0 ? "none" : string.Join(", ", selection.ReplacesTypeKeys))})");
        foreach (var conflict in result.Conflicts)
            Console.WriteLine($"  CONFLICT {conflict.CapabilityKey}: {string.Join(", ", conflict.CandidateTypeKeys)} - {conflict.Reason}");
        foreach (var error in result.Errors) Console.WriteLine($"  ERROR: {error}");
    }

    private static void RenderValidation(PlanValidation? validation, Action<string> write)
    {
        if (validation is null)
        {
            return;
        }

        write($"validation=valid:{validation.Valid.ToString().ToLowerInvariant()},covered:{validation.CoveredImpacts},accepted:{validation.AcceptedImpacts},openDecisions:{validation.OpenDecisions}");
        foreach (var error in validation.Errors)
        {
            write($"validationError={Clean(error)}");
        }

        foreach (var warning in validation.Warnings)
        {
            write($"validationWarning={Clean(warning)}");
        }
    }

    private static string Clean(string value)
        => value.Replace(';', ',').Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static string HumanDependencySuffix(IReadOnlyList<string> dependencies)
        => dependencies.Count switch
        {
            0 => string.Empty,
            <= 4 => $"; after {string.Join(", ", dependencies)}",
            _ => $"; after {dependencies.Count} prerequisite tasks",
        };
}

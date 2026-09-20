using System.CommandLine;
using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Brd;

public sealed partial class BrdModule
{
    private static Command CreateFeatureWizardCommand(FeatureIntakeService service)
    {
        var root = new Command("wizard", "Review a proposed feature against its product baseline in resumable pages.");
        root.Subcommands.Add(CreateFeatureSourceUpdateCommand(service));
        root.Subcommands.Add(CreateFeatureScreensCommand(service));
        root.Subcommands.Add(CreateFeatureArchitectureCommand(service));
        var navigation = new Command("navigation", "List the product, repositories, feature definitions and proposed repository work.");
        var navigationWorkspace = WorkspaceOption(); var navigationFormat = FormatOption();
        navigation.Options.Add(navigationWorkspace); navigation.Options.Add(navigationFormat);
        navigation.SetAction(parse =>
        {
            var format = GetFormat(parse.GetValue(navigationFormat)); if (format is null) return 2;
            var result = service.Navigation(parse.GetValue(navigationWorkspace) ?? Directory.GetCurrentDirectory());
            if (format == "json") Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            else
            {
                Console.WriteLine($"product={Clean(result.Workspace?.Product?.Name ?? "")};features={result.Features.Count};repositories={result.Workspace?.Repositories.Count ?? 0}");
                foreach (var feature in result.Features) Console.WriteLine($"feature={feature.Plan.Slug};title={Clean(feature.Plan.Title)};status={feature.Status};reviewedPages={feature.ReviewedPages};totalPages={feature.TotalPages};repositoryWork={feature.RepositoryWork.Count}");
                foreach (var error in result.Errors) Console.WriteLine("error=" + Clean(error));
            }
            return result.ExitCode;
        });
        root.Subcommands.Add(navigation);
        var list = new Command("list", "List saved feature requests.");
        var listWorkspace = WorkspaceOption(); var listFormat = FormatOption();
        list.Options.Add(listWorkspace); list.Options.Add(listFormat);
        list.SetAction(parse =>
        {
            var format = GetFormat(parse.GetValue(listFormat)); if (format is null) return 2;
            var requests = service.Requests(parse.GetValue(listWorkspace) ?? Directory.GetCurrentDirectory());
            if (format == "json") Console.WriteLine(JsonSerializer.Serialize(new { requests }, JsonOptions));
            else foreach (var request in requests) Console.WriteLine($"feature={request.Slug};title={Clean(request.Title)};path={Clean(request.RequestPath)}");
            return 0;
        });
        root.Subcommands.Add(list);
        foreach (var operation in new[] { "status", "save" })
        {
            var command = new Command(operation, operation == "status" ? "Report feature pages and exact next steps." : "Save reviewed answers for one feature page.");
            var workspace = WorkspaceOption(); var format = FormatOption();
            var slug = new Option<string>("--slug") { Required = operation == "status" };
            var input = new Option<string>("--input") { Required = operation == "save" };
            command.Options.Add(workspace); command.Options.Add(format);
            command.Options.Add(operation == "status" ? slug : input);
            command.SetAction(parse =>
            {
                var selected = GetFormat(parse.GetValue(format)); if (selected is null) return 2;
                CisFeatureWizardResult result;
                try
                {
                    var target = parse.GetValue(workspace) ?? Directory.GetCurrentDirectory();
                    if (operation == "status") result = service.Wizard(target, parse.GetValue(slug)!);
                    else
                    {
                        var file = parse.GetValue(input)!;
                        if (new FileInfo(file).Length > 1_048_576) throw new InvalidDataException("Feature answers exceed 1 MiB.");
                        var answers = JsonSerializer.Deserialize<CisFeatureWizardAnswer>(File.ReadAllText(file), JsonOptions)
                            ?? throw new InvalidDataException("Feature answers are missing.");
                        result = service.SaveWizard(target, answers);
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
                { result = new("invalid", null, null, false, false, [], [e.Message], false); }
                if (selected == "json") Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
                else
                {
                    Console.WriteLine($"status={result.Status};exitCode={result.ExitCode};reviewed={result.Reviewed};applied={result.Applied}");
                    foreach (var page in result.Pages) Console.WriteLine($"page={page.Id};status={page.Status};attention={Clean(string.Join("; ", page.Attention))}");
                    foreach (var error in result.Errors) Console.WriteLine("error=" + Clean(error));
                }
                return result.ExitCode;
            });
            root.Subcommands.Add(command);
        }
        return root;
    }

    private static Command CreateFeatureArchitectureCommand(FeatureIntakeService service)
    {
        var root = new Command("architecture", "Generate and inspect feature-specific C4 diagrams against the product architecture.");
        foreach (var operation in new[] { "status", "prepare" })
        {
            var command = new Command(operation, operation == "prepare" ? "Generate draft C4 views with a local model." : "Check derived diagrams without model generation.");
            var workspace = WorkspaceOption(); var format = FormatOption();
            var slug = new Option<string>("--slug") { Required = true };
            var revision = new Option<string?>("--expected-revision") { Required = operation == "prepare" };
            command.Options.Add(workspace); command.Options.Add(format); command.Options.Add(slug); command.Options.Add(revision);
            command.SetAction(parse =>
            {
                var selected = GetFormat(parse.GetValue(format)); if (selected is null) return 2;
                var result = service.Architecture(parse.GetValue(workspace) ?? Directory.GetCurrentDirectory(), parse.GetValue(slug)!, operation == "prepare", parse.GetValue(revision));
                if (selected == "json") Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
                else
                {
                    Console.WriteLine($"status={result.Status};diagrams={result.Diagrams.Count};cached={result.Cached};provider={result.Provider};model={result.Model};exitCode={result.ExitCode}");
                    foreach (var diagram in result.Diagrams) Console.WriteLine($"diagram={diagram.Id};level={diagram.Level};title={Clean(diagram.Title)}");
                    foreach (var warning in result.Warnings) Console.WriteLine("warning=" + Clean(warning));
                    foreach (var error in result.Errors) Console.WriteLine("error=" + Clean(error));
                }
                return result.ExitCode;
            });
            root.Subcommands.Add(command);
        }
        return root;
    }

    private static Command CreateFeatureScreensCommand(FeatureIntakeService service)
    {
        var root = new Command("screens", "Generate and inspect proposed feature screens from the BRD, experience answers and existing UI baseline.");
        foreach (var operation in new[] { "status", "prepare" })
        {
            var command = new Command(operation, operation == "prepare" ? "Generate draft screens using a local model." : "Check cached screens without model generation.");
            var workspace = WorkspaceOption(); var format = FormatOption();
            var slug = new Option<string>("--slug") { Required = true };
            var revision = new Option<string?>("--expected-revision") { Required = operation == "prepare" };
            var screenKey = new Option<string?>("--screen") { Description = "Regenerate only this reviewed screen key with its saved amendment request." };
            command.Options.Add(workspace); command.Options.Add(format); command.Options.Add(slug); command.Options.Add(revision);
            command.Options.Add(screenKey);
            command.SetAction(parse =>
            {
                var selected = GetFormat(parse.GetValue(format)); if (selected is null) return 2;
                var result = service.Screens(parse.GetValue(workspace) ?? Directory.GetCurrentDirectory(), parse.GetValue(slug)!, operation == "prepare", parse.GetValue(revision), parse.GetValue(screenKey));
                if (selected == "json") Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
                else
                {
                    Console.WriteLine($"status={result.Status};screens={result.Screens.Count};cached={result.Cached};provider={result.Provider};model={result.Model};exitCode={result.ExitCode}");
                    foreach (var screen in result.Screens) Console.WriteLine($"screen={screen.Plan.Id};title={Clean(screen.Plan.Title)};frontend={screen.Plan.FrontendType};route={Clean(screen.Plan.Route)}");
                    foreach (var warning in result.Warnings) Console.WriteLine("warning=" + Clean(warning));
                    foreach (var error in result.Errors) Console.WriteLine("error=" + Clean(error));
                }
                return result.ExitCode;
            });
            root.Subcommands.Add(command);
        }
        return root;
    }

    private static Command CreateFeatureIntakeCommand(FeatureIntakeService service)
    {
        var command = new Command("intake", "Introduce a feature from a prepared BRD and create or connect its implementation repository.");
        var input = new Option<string>("--input") { Required = true, Description = "JSON request with title, slug, sourcePath, repositoryMode, repositoryPath, documentationRoot, integrationRepositories and actor." };
        var dryRun = new Option<bool>("--dry-run") { Description = "Preview the proposed setup without creating files." };
        var yes = new Option<bool>("--yes") { Description = "Apply the reviewed setup preview." };
        var expected = new Option<string?>("--expected-plan") { Description = "Exact planHash returned by the preview." };
        var workspace = WorkspaceOption(); var format = FormatOption();
        command.Options.Add(input); command.Options.Add(dryRun); command.Options.Add(yes);
        command.Options.Add(expected); command.Options.Add(workspace); command.Options.Add(format);
        command.SetAction(parse =>
        {
            var selected = GetFormat(parse.GetValue(format)); if (selected is null) return 2;
            CisFeatureIntakeResult result;
            try
            {
                var file = parse.GetValue(input)!;
                if (new FileInfo(file).Length > 65_536) throw new InvalidDataException("Feature intake input exceeds 64 KiB.");
                var request = JsonSerializer.Deserialize<CisFeatureIntakeRequest>(File.ReadAllText(file), JsonOptions)
                    ?? throw new InvalidDataException("Feature intake input is empty.");
                result = service.Import(parse.GetValue(workspace) ?? Directory.GetCurrentDirectory(), request,
                    parse.GetValue(dryRun), parse.GetValue(yes), parse.GetValue(expected));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
            { result = new("invalid", null, [exception.Message], [], false); }
            if (selected == "json") Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            else
            {
                Console.WriteLine($"status={result.Status};exitCode={result.ExitCode};applied={result.Applied.ToString().ToLowerInvariant()};confirmationRequired={result.ConfirmationRequired.ToString().ToLowerInvariant()}");
                if (result.Plan is { } plan)
                {
                    Console.WriteLine($"feature={Clean(plan.Title)};request={Clean(plan.RequestPath)};repository={Clean(plan.RepositoryPath)};mode={plan.RepositoryMode};openDecisions={plan.OpenDecisions.Count}");
                    Console.WriteLine($"planHash={plan.PlanHash}");
                }
                foreach (var error in result.Errors) Console.WriteLine("error=" + Clean(error));
                foreach (var warning in result.Warnings) Console.WriteLine("warning=" + Clean(warning));
            }
            return result.ExitCode;
        });
        return command;
    }

    private static Command CreateFeatureSourceUpdateCommand(FeatureIntakeService service)
    {
        var command = new Command("reimport", "Preview or apply an updated BRD to an existing feature, preserving answers and source history.");
        var slug = new Option<string>("--slug") { Required = true };
        var source = new Option<string>("--source") { Required = true, Description = "Updated local Markdown BRD." };
        var actor = new Option<string>("--actor") { Required = true };
        var dryRun = new Option<bool>("--dry-run");
        var yes = new Option<bool>("--yes");
        var expected = new Option<string?>("--expected-plan");
        var workspace = WorkspaceOption(); var format = FormatOption();
        command.Options.Add(slug); command.Options.Add(source); command.Options.Add(actor);
        command.Options.Add(dryRun); command.Options.Add(yes); command.Options.Add(expected);
        command.Options.Add(workspace); command.Options.Add(format);
        command.SetAction(parse =>
        {
            var selected = GetFormat(parse.GetValue(format)); if (selected is null) return 2;
            var result = service.UpdateSource(parse.GetValue(workspace) ?? Directory.GetCurrentDirectory(),
                parse.GetValue(slug)!, parse.GetValue(source)!, parse.GetValue(actor)!,
                parse.GetValue(dryRun), parse.GetValue(yes), parse.GetValue(expected));
            if (selected == "json") Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            else
            {
                Console.WriteLine($"status={result.Status};exitCode={result.ExitCode};applied={result.Applied};confirmationRequired={result.ConfirmationRequired}");
                if (result.Plan is { } plan)
                {
                    Console.WriteLine($"feature={plan.Slug};planHash={plan.PlanHash};addedDecisions={plan.AddedDecisions.Count};removedDecisions={plan.RemovedDecisions.Count};retainedDecisionAnswers={plan.RetainedDecisionAnswers}");
                    Console.WriteLine($"history={Clean(plan.PreviousRequestPath)};reviewPages={Clean(string.Join(", ", plan.PagesRequiringReview))}");
                }
                foreach (var error in result.Errors) Console.WriteLine("error=" + Clean(error));
            }
            return result.ExitCode;
        });
        return command;
    }
}

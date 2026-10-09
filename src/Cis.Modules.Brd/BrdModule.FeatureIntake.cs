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
        root.Subcommands.Add(CreateFeatureDeliveryCommand(service));
        root.Subcommands.Add(CreateFeatureStoryCommand(service));
        var startBacklog = new Command("start-backlog", "Open an approved backlog outcome in the feature-definition wizard.");
        var startWorkspace = WorkspaceOption(); var startFormat = FormatOption();
        var startItem = new Option<string>("--item") { Required = true };
        var startActor = new Option<string>("--actor") { Required = true };
        var startHash = new Option<string>("--expected-input-hash") { Required = true };
        startBacklog.Options.Add(startWorkspace); startBacklog.Options.Add(startFormat);
        startBacklog.Options.Add(startItem); startBacklog.Options.Add(startActor); startBacklog.Options.Add(startHash);
        startBacklog.SetAction(parse =>
        {
            var format = GetFormat(parse.GetValue(startFormat)); if (format is null) return 2;
            var result = service.StartBacklogFeature(parse.GetValue(startWorkspace) ?? Directory.GetCurrentDirectory(),
                parse.GetValue(startItem)!, parse.GetValue(startActor)!, parse.GetValue(startHash)!);
            if (format == "json") Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            else { Console.WriteLine($"status={Clean(result.Status)};feature={Clean(result.Plan?.Slug)}"); foreach (var error in result.Errors) Console.WriteLine("error=" + Clean(error)); }
            return result.ExitCode;
        });
        root.Subcommands.Add(startBacklog);
        var navigation = new Command("navigation", "List the product, feature stories and their proposed repository links.");
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
                foreach (var feature in result.Features)
                {
                    Console.WriteLine($"feature={feature.Plan.Slug};title={Clean(feature.Plan.Title)};status={feature.Status};reviewedPages={feature.ReviewedPages};totalPages={feature.TotalPages};stories={feature.Stories.Count}");
                    foreach (var story in feature.Stories) Console.WriteLine($"story={story.Id};feature={feature.Plan.Slug};title={Clean(story.Title)};status={Clean(story.Status)};repositories={Clean(string.Join(',', story.RepositoryIds))}");
                }
                foreach (var error in result.Errors) Console.WriteLine("error=" + Clean(error));
                foreach (var item in result.BacklogFeatures) Console.WriteLine($"backlogFeature={item.Id};title={Clean(item.Title)};status={Clean(item.Status)}");
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

    private static Command CreateFeatureStoryCommand(FeatureIntakeService service)
    {
        var root = new Command("story", "Read a story definition and acceptance criteria, or generate its proposed tasks.");
        foreach (var operation in new[] { "status", "prepare" })
        {
            var command = new Command(operation, operation == "prepare" ? "Generate this story's task breakdown with a selected model; automatic selection stays local." : "Read this story and its current task breakdown.");
            var workspace = WorkspaceOption(); var format = FormatOption();
            var slug = new Option<string>("--slug") { Required = true };
            var story = new Option<string>("--story") { Required = true };
            var hash = new Option<string?>("--expected-input-hash") { Required = operation == "prepare" };
            var provider = new Option<string?>("--provider") { Description = "Choose a provider and regenerate the task plan." };
            var model = new Option<string?>("--model");
            var allowRemote = new Option<bool>("--allow-remote") { Description = "Authorize sending this story, saved direction and selected code excerpts to the chosen remote model." };
            command.Options.Add(workspace); command.Options.Add(format); command.Options.Add(slug); command.Options.Add(story); command.Options.Add(hash);
            command.Options.Add(provider); command.Options.Add(model); command.Options.Add(allowRemote);
            command.SetAction(parse =>
            {
                var selected = GetFormat(parse.GetValue(format)); if (selected is null) return 2;
                var result = service.Story(parse.GetValue(workspace) ?? Directory.GetCurrentDirectory(), parse.GetValue(slug)!, parse.GetValue(story)!, operation == "prepare", parse.GetValue(hash), parse.GetValue(provider), parse.GetValue(model), parse.GetValue(allowRemote));
                if (selected == "json") Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
                else
                {
                    Console.WriteLine($"status={result.Status};feature={result.Slug};story={result.StoryId};title={Clean(result.Story?.Title ?? "")};tasks={result.Tasks.Count}");
                    Console.WriteLine($"provider={Clean(result.Provider ?? "")};model={Clean(result.Model ?? "")}");
                    Console.WriteLine("definition=" + Clean(result.Definition));
                    foreach (var criterion in result.Story?.Requirements ?? []) Console.WriteLine("acceptance=" + Clean(criterion));
                    foreach (var task in result.Tasks) Console.WriteLine($"task={task.Id};title={Clean(task.Title)};repositories={Clean(string.Join(',', task.RepositoryIds))}");
                    foreach (var error in result.Errors) Console.WriteLine("error=" + Clean(error));
                }
                return result.ExitCode;
            });
            root.Subcommands.Add(command);
        }
        foreach (var operation in new[] { "approve", "start", "complete", "completion-context" })
        {
            var command = new Command(operation, operation == "approve" ? "Approve the exact reviewed story task plan." : operation == "start" ? "Record the start of a dependency-ready task; this does not launch an agent." : operation == "completion-context" ? "Print missing-gate completion templates for each adopted story participant without completing the task." : "Record verified task completion and unlock dependent tasks.");
            var workspace = WorkspaceOption(); var format = FormatOption();
            var slug = new Option<string>("--slug") { Required = true };
            var story = new Option<string>("--story") { Required = true };
            var planHash = new Option<string>("--expected-plan-hash") { Required = true };
            var revision = new Option<string>("--expected-revision") { Required = true };
            var actor = new Option<string>("--actor") { Required = true };
            var reason = new Option<string?>("--reason") { Required = operation == "approve" };
            var task = new Option<string?>("--task") { Required = operation != "approve" };
            var evidence = new Option<string?>("--evidence") { Required = operation == "complete" };
            var verified = new Option<bool>("--criteria-verified");
            command.Options.Add(workspace); command.Options.Add(format); command.Options.Add(slug); command.Options.Add(story);
            command.Options.Add(planHash); command.Options.Add(revision); command.Options.Add(actor); command.Options.Add(reason);
            command.Options.Add(task); command.Options.Add(evidence); command.Options.Add(verified);
            command.SetAction(parse =>
            {
                var selected = GetFormat(parse.GetValue(format)); if (selected is null) return 2;
                var result = service.UpdateStoryWorkflow(parse.GetValue(workspace) ?? Directory.GetCurrentDirectory(), parse.GetValue(slug)!,
                    parse.GetValue(story)!, operation, parse.GetValue(planHash)!, parse.GetValue(revision)!, parse.GetValue(actor)!,
                    parse.GetValue(reason), parse.GetValue(task), parse.GetValue(evidence), parse.GetValue(verified));
                if (selected == "json") Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
                else
                {
                    Console.WriteLine($"status={result.Status};planState={result.PlanState};planHash={result.PlanHash};revision={result.Revision}");
                    foreach (var progress in result.TaskProgress) Console.WriteLine($"task={progress.Id};status={progress.Status};blocked={Clean(progress.BlockedReason ?? "")}");
                    foreach (var context in result.CompletionContexts)
                    {
                        Console.WriteLine($"repository={context.RepositoryId};receipt={context.ReceiptPath}");
                        if (context.TemplateJson is not null) Console.WriteLine(context.TemplateJson);
                    }
                    foreach (var error in result.Errors) Console.WriteLine("error=" + Clean(error));
                }
                return result.ExitCode;
            });
            root.Subcommands.Add(command);
        }
        root.Subcommands.Add(CreateStoryExecutionCommand(service, false));
        root.Subcommands.Add(CreateStoryExecutionCommand(service, true));
        root.Subcommands.Add(CreateStoryFeedbackCommand(service, false));
        root.Subcommands.Add(CreateStoryFeedbackCommand(service, true));
        return root;
    }

    private static Command CreateStoryFeedbackCommand(FeatureIntakeService service, bool suggest)
    {
        var command = new Command(suggest ? "feedback-suggest" : "feedback-save", suggest
            ? "Suggest evidence-backed answers to task review findings without accepting them." : "Save human answers to the exact task review without approving or executing work.");
        var workspace = WorkspaceOption(); var format = FormatOption();
        var input = new Option<string?>("--input") { Required = !suggest };
        var slug = new Option<string?>("--slug") { Required = suggest };
        var story = new Option<string?>("--story") { Required = suggest };
        var task = new Option<string?>("--task") { Required = suggest };
        var revision = new Option<string?>("--expected-revision") { Required = suggest };
        var provider = new Option<string?>("--provider"); var model = new Option<string?>("--model");
        var remote = new Option<bool>("--allow-remote");
        foreach (var option in new Option[] { workspace, format, input, slug, story, task, revision, provider, model, remote }) command.Options.Add(option);
        command.SetAction(parse =>
        {
            var selected = GetFormat(parse.GetValue(format)); if (selected is null) return 2;
            CisFeatureStoryResult result;
            try
            {
                if (suggest) result = service.SuggestStoryFeedback(parse.GetValue(workspace) ?? Directory.GetCurrentDirectory(),
                    parse.GetValue(slug)!, parse.GetValue(story)!, parse.GetValue(task)!, parse.GetValue(revision)!, parse.GetValue(provider), parse.GetValue(model), parse.GetValue(remote));
                else
                {
                    var path = parse.GetValue(input)!;
                    if (!File.Exists(path) || new FileInfo(path).Length > 262_144) throw new InvalidDataException("Select a response file no larger than 256 KiB.");
                    var request = JsonSerializer.Deserialize<CisStoryFeedbackRequest>(File.ReadAllText(path), JsonOptions)
                        ?? throw new InvalidDataException("The response file is empty.");
                    result = service.SaveStoryFeedback(parse.GetValue(workspace) ?? Directory.GetCurrentDirectory(), request);
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException)
            { result = new("failed", parse.GetValue(slug) ?? "", parse.GetValue(story) ?? "", null, "", "", null, [], [], [error.Message]); }
            if (selected == "json") Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            else
            {
                Console.WriteLine($"status={result.Status};planState={result.PlanState};revision={result.Revision}");
                foreach (var feedback in result.TaskProgress.SelectMany(item => item.ReviewFeedback ?? []))
                    Console.WriteLine($"finding={feedback.Id};answered={!string.IsNullOrWhiteSpace(feedback.Answer)};suggested={!string.IsNullOrWhiteSpace(feedback.SuggestedAnswer)}");
                foreach (var error in result.Errors) Console.WriteLine("error=" + Clean(error));
            }
            return result.ExitCode;
        });
        return command;
    }

    private static Command CreateStoryExecutionCommand(FeatureIntakeService service, bool execute)
    {
        var command = new Command(execute ? "execute" : "execution-plan", execute
            ? "Implement an approved task and review it with distinct automatically selected models." : "Choose implementation and review models for this approved task without executing it.");
        var workspace = WorkspaceOption(); var format = FormatOption();
        var slug = new Option<string>("--slug") { Required = true }; var story = new Option<string>("--story") { Required = true };
        var task = new Option<string>("--task") { Required = true };
        var plan = new Option<string?>("--expected-plan-hash") { Required = execute };
        var revision = new Option<string?>("--expected-revision") { Required = execute };
        var selection = new Option<string?>("--expected-execution-hash") { Required = execute };
        var actor = new Option<string>("--actor") { Required = execute };
        var remote = new Option<bool>("--allow-remote");
        foreach (var option in new Option[] { workspace, format, slug, story, task, plan, revision, selection, actor, remote }) command.Options.Add(option);
        command.SetAction(parse =>
        {
            var selected = GetFormat(parse.GetValue(format)); if (selected is null) return 2;
            using var cancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler cancel = (_, args) => { args.Cancel = true; cancellation.Cancel(); };
            Console.CancelKeyPress += cancel;
            try
            {
                var result = service.ExecuteStoryTask(parse.GetValue(workspace) ?? Directory.GetCurrentDirectory(), parse.GetValue(slug)!, parse.GetValue(story)!,
                    parse.GetValue(task)!, execute, parse.GetValue(plan), parse.GetValue(revision), parse.GetValue(selection), parse.GetValue(actor) ?? "", parse.GetValue(remote), cancellation.Token,
                    item => { if (item.Kind != "provider-heartbeat") Console.Error.WriteLine($"agent-event={Clean(item.Kind)};message={Clean(item.Message)}"); });
                if (selected == "json") Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
                else
                {
                    Console.WriteLine($"status={result.Status};planState={result.PlanState};selection={result.ExecutionPlan?.Hash}");
                    if (result.ExecutionPlan is { } models) Console.WriteLine($"complexity={models.Complexity};implementation={models.Implementation?.Provider}/{models.Implementation?.Model};review={models.Review?.Provider}/{models.Review?.Model}");
                    foreach (var error in result.Errors) Console.WriteLine("error=" + Clean(error));
                }
                return result.ExitCode;
            }
            finally { Console.CancelKeyPress -= cancel; }
        });
        return command;
    }

    private static Command CreateFeatureDeliveryCommand(FeatureIntakeService service)
    {
        var root = new Command("delivery", "Reconcile feature requirements with existing owned implementation.");
        foreach (var operation in new[] { "status", "prepare" })
        {
            var command = new Command(operation, operation == "prepare" ? "Prepare a local model assessment of existing capability and remaining work." : "Check the cached implementation assessment without model generation.");
            var workspace = WorkspaceOption(); var format = FormatOption();
            var slug = new Option<string>("--slug") { Required = true };
            var revision = new Option<string?>("--expected-revision") { Required = operation == "prepare" };
            command.Options.Add(workspace); command.Options.Add(format); command.Options.Add(slug); command.Options.Add(revision);
            command.SetAction(parse =>
            {
                var selected = GetFormat(parse.GetValue(format)); if (selected is null) return 2;
                var result = service.Delivery(parse.GetValue(workspace) ?? Directory.GetCurrentDirectory(), parse.GetValue(slug)!, operation == "prepare", parse.GetValue(revision));
                if (selected == "json") Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
                else
                {
                    Console.WriteLine($"status={result.Status};stories={result.Stories.Count};cached={result.Cached};provider={result.Provider};model={result.Model};exitCode={result.ExitCode}");
                    foreach (var story in result.Stories) Console.WriteLine($"story={story.Id};title={Clean(story.Title)};treatment={story.Treatment};owners={Clean(string.Join(',', story.Owners))}");
                    foreach (var warning in result.Warnings) Console.WriteLine("warning=" + Clean(warning));
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

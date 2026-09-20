using System.CommandLine;
using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Repository;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cis.Modules.Brd;

public sealed partial class BrdModule : ICisModule
{
    private static readonly string[] Formats = ["human", "json", "agent"];
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public string Name => "brd";

    public string Description => "Discover, reconcile, validate, and approve canonical business requirements.";

    public void RegisterServices(IServiceCollection services)
    {
        services.TryAddSingleton<DocumentationCatalogMerger>();
        services.AddSingleton<BrdService>();
        services.AddSingleton(provider => new BrdSourceSummaryService(provider.GetRequiredService<BrdService>(),
            provider.GetRequiredService<ICisWorkspaceRegistry>(), provider.GetService<ICisTextGenerationService>() ?? new UnavailableTextGenerationService()));
        services.AddSingleton<ICisBrdSourceEvidenceReconciler>(provider => provider.GetRequiredService<BrdService>());
        services.AddSingleton<BrdReviewDispositionService>();
        services.AddSingleton<ICisBrdReviewFreshness>(provider => provider.GetRequiredService<BrdReviewDispositionService>());
        services.AddSingleton(provider => new BrdQuestionGuidanceService(
            provider.GetRequiredService<BrdService>(),
            provider.GetRequiredService<ICisWorkspaceRegistry>(),
            provider.GetService<ICisTextGenerationService>()));
        services.AddSingleton<BrdBacklogService>();
        services.AddSingleton<FeatureIntakeService>();
        services.AddSingleton<ICisFeatureApprovalAuthority, BrdFeatureApprovalAuthority>();
    }

    public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services)
    {
        var service = services.GetRequiredService<BrdService>();
        var brd = new Command(Name, Description);
        brd.Subcommands.Add(CreateDiscoverCommand(service));
        brd.Subcommands.Add(CreateInitCommand(service));
        brd.Subcommands.Add(CreateSimpleCommand(
            "reconcile",
            "Reconcile new or changed BRD and feature-specification evidence into the canonical BRD review workflow.",
            service.Reconcile));
        brd.Subcommands.Add(CreateSimpleCommand(
            "status",
            "Report effective BRD lifecycle and drift status.",
            service.Status));
        brd.Subcommands.Add(CreateSimpleCommand(
            "validate",
            "Validate BRD structure, source assessment, graph freshness, and participant baselines.",
            service.Validate));
        brd.Subcommands.Add(CreateQuestionsCommand(service,
            services.GetRequiredService<BrdQuestionGuidanceService>()));
        brd.Subcommands.Add(CreateReviewCommand(services.GetRequiredService<BrdReviewDispositionService>()));
        brd.Subcommands.Add(CreateApproveCommand(service));
        brd.Subcommands.Add(CreateSourceDecisionsCommand(service, services.GetRequiredService<BrdSourceSummaryService>()));
        brd.Subcommands.Add(CreateBacklogCommand(services.GetRequiredService<BrdBacklogService>()));
        var feature = CreateFeatureCommand(services.GetRequiredService<BrdBacklogService>());
        feature.Subcommands.Add(CreateFeatureIntakeCommand(services.GetRequiredService<FeatureIntakeService>()));
        feature.Subcommands.Add(CreateFeatureWizardCommand(services.GetRequiredService<FeatureIntakeService>()));
        brd.Subcommands.Add(feature);
        commands.Add(brd);
    }

    private static Command CreateSourceDecisionsCommand(BrdService service, BrdSourceSummaryService summaries)
    {
        var sources = new Command("sources", "Record explicit human assessments of BRD source documents.");
        var assess = new Command("assess", "Save a reviewed batch of source decisions atomically without approving the BRD.");
        var input = new Option<string>("--input") { Required = true, Description = "JSON array of id, assessment, reason and reviewToken from current BRD status." };
        var workspace = WorkspaceOption(); var format = FormatOption();
        assess.Options.Add(input); assess.Options.Add(workspace); assess.Options.Add(format);
        assess.SetAction(parse =>
        {
            var selected = GetFormat(parse.GetValue(format)); if (selected is null) return 2;
            BrdResult result;
            var root = parse.GetValue(workspace) ?? Directory.GetCurrentDirectory();
            try
            {
                var file = parse.GetValue(input)!;
                if (new FileInfo(file).Length > 1_048_576) throw new JsonException("Decision input exceeds 1 MiB.");
                var decisions = JsonSerializer.Deserialize<CisBrdSourceDecision[]>(File.ReadAllText(file), JsonOptions)
                    ?? throw new JsonException("Expected a source-decision array.");
                result = service.AssessSources(root, decisions);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
            {
                result = new("invalid", root, null, null, null, null, [], [error.Message], Applied: false);
            }
            Render(result, selected); return result.ExitCode;
        });
        sources.Subcommands.Add(assess);
        var summarize = new Command("summarize", "Generate missing document summaries with a local model and cache them by source content.");
        var summaryWorkspace = WorkspaceOption(); var summaryFormat = FormatOption();
        var summaryLimit = new Option<int>("--limit") { DefaultValueFactory = _ => 100, Description = "Maximum missing summaries to generate (1–100)." };
        summarize.Options.Add(summaryWorkspace); summarize.Options.Add(summaryFormat); summarize.Options.Add(summaryLimit);
        summarize.SetAction(parse =>
        {
            var selected = GetFormat(parse.GetValue(summaryFormat)); if (selected is null) return 2;
            var result = summaries.Generate(parse.GetValue(summaryWorkspace) ?? Directory.GetCurrentDirectory(), parse.GetValue(summaryLimit));
            if (selected == "json") Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            else
            {
                Console.WriteLine(selected == "agent" ? $"status={result.Status};exitCode={result.ExitCode};generated={result.Generated};reused={result.Reused};sources={result.Sources.Count}"
                    : $"Source summaries: {result.Generated} generated, {result.Reused} cached.");
                foreach (var source in result.Sources) Console.WriteLine(selected == "agent"
                    ? $"source={Clean(source.Id)};kind={source.Summary.Kind};summary={Clean(source.Summary.Text)}" : $"{source.Id}: {source.Summary.Text}");
                foreach (var warning in result.Warnings) Console.WriteLine(selected == "agent" ? $"warning={Clean(warning)}" : $"Warning: {warning}");
                foreach (var error in result.Errors) Console.WriteLine(selected == "agent" ? $"error={Clean(error)}" : $"Error: {error}");
            }
            return result.ExitCode;
        });
        sources.Subcommands.Add(summarize); return sources;
    }

    private static Command CreateReviewCommand(BrdReviewDispositionService service)
    {
        var review = new Command("review", "Independently disposition and approve advisory BRD review recommendations.");

        var init = new Command("init", "Create the canonical human disposition record from one successful agent review.");
        var initRun = new Argument<string>("run-id"); var initWorkspace = WorkspaceOption(); var initFormat = FormatOption();
        init.Arguments.Add(initRun); init.Options.Add(initWorkspace); init.Options.Add(initFormat);
        init.SetAction(result => RenderReviewDisposition(service.Initialize(
            result.GetValue(initWorkspace) ?? Directory.GetCurrentDirectory(), result.GetValue(initRun) ?? string.Empty),
            result.GetValue(initFormat)!));
        review.Subcommands.Add(init);

        var status = new Command("status", "Show every recommendation and its independent human disposition.");
        var statusRun = new Argument<string>("run-id"); var statusWorkspace = WorkspaceOption(); var statusFormat = FormatOption();
        status.Arguments.Add(statusRun); status.Options.Add(statusWorkspace); status.Options.Add(statusFormat);
        status.SetAction(result => RenderReviewDisposition(service.Status(
            result.GetValue(statusWorkspace) ?? Directory.GetCurrentDirectory(), result.GetValue(statusRun) ?? string.Empty),
            result.GetValue(statusFormat)!));
        review.Subcommands.Add(status);

        var freshness = new Command("freshness", "Classify whether a successful independent review still covers the current BRD.");
        var freshnessRun = new Argument<string>("run-id"); var freshnessWorkspace = WorkspaceOption(); var freshnessFormat = FormatOption();
        freshness.Arguments.Add(freshnessRun); freshness.Options.Add(freshnessWorkspace); freshness.Options.Add(freshnessFormat);
        freshness.SetAction(result => RenderReviewFreshness(service.Freshness(
            result.GetValue(freshnessWorkspace) ?? Directory.GetCurrentDirectory(), result.GetValue(freshnessRun) ?? string.Empty),
            result.GetValue(freshnessFormat)!));
        review.Subcommands.Add(freshness);

        var decide = new Command("decide", "Approve one recommendation as written or with exact human-edited remediation text; legacy rejection remains supported with a rationale.");
        var decideRun = new Argument<string>("run-id"); var finding = new Argument<string>("finding-id");
        var decision = new Option<string>("--decision") { Required = true, Description = "accepted or rejected" };
        var actor = new Option<string>("--actor") { Required = true };
        var rationale = new Option<string?>("--reason") { Description = "Required only for a legacy rejected disposition." };
        var approvedRecommendation = new Option<string?>("--approved-recommendation")
        {
            Description = "Exact remediation text approved by the human. Omit to approve the reviewer's recommendation as written."
        };
        var decideWorkspace = WorkspaceOption(); var decideFormat = FormatOption();
        decide.Arguments.Add(decideRun); decide.Arguments.Add(finding);
        foreach (var option in new Option[] { decision, actor, rationale, approvedRecommendation, decideWorkspace, decideFormat }) decide.Options.Add(option);
        decide.SetAction(result => RenderReviewDisposition(service.Decide(
            result.GetValue(decideWorkspace) ?? Directory.GetCurrentDirectory(), result.GetValue(decideRun) ?? string.Empty,
            result.GetValue(finding) ?? string.Empty, result.GetValue(decision) ?? string.Empty,
            result.GetValue(actor) ?? string.Empty, result.GetValue(rationale) ?? string.Empty,
            result.GetValue(approvedRecommendation)), result.GetValue(decideFormat)!));
        review.Subcommands.Add(decide);

        var acceptAll = new Command("accept-all", "Approve every pending recommendation as written and atomically lock the complete bounded set.");
        var acceptAllRun = new Argument<string>("run-id");
        var acceptAllActor = new Option<string>("--actor") { Required = true };
        var acceptAllWorkspace = WorkspaceOption(); var acceptAllFormat = FormatOption();
        acceptAll.Arguments.Add(acceptAllRun);
        foreach (var option in new Option[] { acceptAllActor, acceptAllWorkspace, acceptAllFormat }) acceptAll.Options.Add(option);
        acceptAll.SetAction(result => RenderReviewDisposition(service.AcceptAll(
            result.GetValue(acceptAllWorkspace) ?? Directory.GetCurrentDirectory(),
            result.GetValue(acceptAllRun) ?? string.Empty,
            result.GetValue(acceptAllActor) ?? string.Empty), result.GetValue(acceptAllFormat)!));
        review.Subcommands.Add(acceptAll);

        var approve = new Command("approve", "Recover a complete legacy disposition set that predates automatic locking.");
        var approveRun = new Argument<string>("run-id"); var reviewer = new Option<string>("--reviewer") { Required = true };
        var reason = new Option<string?>("--reason") { Description = "Optional legacy approval rationale; the exact approved recommendation text is authoritative." };
        var approveWorkspace = WorkspaceOption(); var approveFormat = FormatOption();
        approve.Arguments.Add(approveRun);
        foreach (var option in new Option[] { reviewer, reason, approveWorkspace, approveFormat }) approve.Options.Add(option);
        approve.SetAction(result => RenderReviewDisposition(service.Approve(
            result.GetValue(approveWorkspace) ?? Directory.GetCurrentDirectory(), result.GetValue(approveRun) ?? string.Empty,
            result.GetValue(reviewer) ?? string.Empty, result.GetValue(reason) ?? string.Empty), result.GetValue(approveFormat)!));
        review.Subcommands.Add(approve);
        return review;
    }

    private static Command CreateQuestionsCommand(BrdService service, BrdQuestionGuidanceService guidanceService)
    {
        var questions = new Command("questions", "List and answer canonical BRD open questions.");
        var list = new Command("list", "List structured and numbered open questions with their answer status.");
        var listWorkspace = WorkspaceOption(); var listFormat = FormatOption();
        list.Options.Add(listWorkspace); list.Options.Add(listFormat);
        list.SetAction(parseResult =>
        {
            var selected = GetFormat(parseResult.GetValue(listFormat)); if (selected is null) return 2;
            var result = service.Questions(parseResult.GetValue(listWorkspace) ?? Directory.GetCurrentDirectory());
            RenderQuestions(result, selected); return result.ExitCode;
        });
        questions.Subcommands.Add(list);

        var guidance = new Command("guidance", "Show every BRD question with deterministic context and current advisory suggestions.");
        var guidanceWorkspace = WorkspaceOption(); var guidanceFormat = FormatOption();
        guidance.Options.Add(guidanceWorkspace); guidance.Options.Add(guidanceFormat);
        guidance.SetAction(parseResult => RenderQuestionGuidance(guidanceService.Guidance(
            parseResult.GetValue(guidanceWorkspace) ?? Directory.GetCurrentDirectory()),
            parseResult.GetValue(guidanceFormat)!));
        questions.Subcommands.Add(guidance);

        var suggest = new Command("suggest", "Generate digest-bound advisory answers from bounded BRD context without answering any question.");
        var suggestionProvider = new Option<string?>("--provider") { Description = "AI provider; omission selects an available local provider only." };
        var suggestionModel = new Option<string?>("--model") { Description = "Optional provider model." };
        var allowRemote = new Option<bool>("--allow-remote") { Description = "Authorize sending the displayed BRD question context to the selected remote provider." };
        var suggestWorkspace = WorkspaceOption(); var suggestFormat = FormatOption();
        foreach (var option in new Option[] { suggestionProvider, suggestionModel, allowRemote, suggestWorkspace, suggestFormat }) suggest.Options.Add(option);
        suggest.SetAction(parseResult => RenderQuestionGuidance(guidanceService.Suggest(
            parseResult.GetValue(suggestWorkspace) ?? Directory.GetCurrentDirectory(),
            parseResult.GetValue(suggestionProvider), parseResult.GetValue(suggestionModel),
            parseResult.GetValue(allowRemote)), parseResult.GetValue(suggestFormat)!));
        questions.Subcommands.Add(suggest);

        var answer = new Command("answer", "Record one human answer and normalize the Open questions section.");
        var id = new Argument<string>("question-id");
        var value = new Option<string>("--answer") { Required = true, Description = "Substantive human answer." };
        var actor = new Option<string>("--actor") { Required = true, Description = "Human actor recorded with the answer." };
        var answerWorkspace = WorkspaceOption(); var answerFormat = FormatOption();
        answer.Arguments.Add(id); answer.Options.Add(value); answer.Options.Add(actor);
        answer.Options.Add(answerWorkspace); answer.Options.Add(answerFormat);
        answer.SetAction(parseResult =>
        {
            var selected = GetFormat(parseResult.GetValue(answerFormat)); if (selected is null) return 2;
            var result = service.AnswerQuestion(parseResult.GetValue(answerWorkspace) ?? Directory.GetCurrentDirectory(),
                parseResult.GetValue(id) ?? string.Empty, parseResult.GetValue(value) ?? string.Empty,
                parseResult.GetValue(actor) ?? string.Empty);
            RenderQuestions(result, selected); return result.ExitCode;
        });
        questions.Subcommands.Add(answer);
        return questions;
    }

    private static Command CreateFeatureCommand(BrdBacklogService service)
    {
        var feature = new Command("feature", "Validate, inspect, and approve a backlog-derived feature specification.");
        feature.Subcommands.Add(CreateFeatureSimpleCommand("validate", "Validate feature structure, traceability, scope, and lifecycle readiness.", service.ValidateFeature));
        feature.Subcommands.Add(CreateFeatureSimpleCommand("status", "Report feature-specification lifecycle and backlog-item currency.", service.FeatureStatus));
        feature.Subcommands.Add(CreateFeatureApproveCommand(service));
        return feature;
    }

    private static Command CreateFeatureSimpleCommand(string name, string description, Func<string, string, BrdFeatureResult> action)
    {
        var command = new Command(name, description);
        var workspace = WorkspaceOption(); var format = FormatOption();
        var item = new Option<string>("--item") { Required = true, Description = "Stable HLT item ID." };
        command.Options.Add(workspace); command.Options.Add(item); command.Options.Add(format);
        command.SetAction(parseResult =>
        {
            var selected = GetFormat(parseResult.GetValue(format)); if (selected is null) return 2;
            var result = action(parseResult.GetValue(workspace) ?? Directory.GetCurrentDirectory(),
                parseResult.GetValue(item) ?? string.Empty);
            RenderFeatureLifecycle(result, selected); return result.ExitCode;
        });
        return command;
    }

    private static Command CreateFeatureApproveCommand(BrdBacklogService service)
    {
        var command = new Command("approve", "Record explicit human approval of a valid, current feature specification.");
        var workspace = WorkspaceOption(); var format = FormatOption();
        var item = new Option<string>("--item") { Required = true, Description = "Stable HLT item ID." };
        var reviewer = new Option<string>("--reviewer") { Required = true, Description = "Human reviewer identity." };
        var reason = new Option<string>("--reason") { Required = true, Description = "Approval rationale." };
        command.Options.Add(workspace); command.Options.Add(item); command.Options.Add(reviewer); command.Options.Add(reason); command.Options.Add(format);
        command.SetAction(parseResult =>
        {
            var selected = GetFormat(parseResult.GetValue(format)); if (selected is null) return 2;
            var result = service.ApproveFeature(parseResult.GetValue(workspace) ?? Directory.GetCurrentDirectory(),
                parseResult.GetValue(item) ?? string.Empty, parseResult.GetValue(reviewer) ?? string.Empty,
                parseResult.GetValue(reason) ?? string.Empty);
            RenderFeatureLifecycle(result, selected); return result.ExitCode;
        });
        return command;
    }

    private static Command CreateBacklogCommand(BrdBacklogService service)
    {
        var backlog = new Command("backlog", "Build and govern a high-level product backlog from the Active BRD and technical intent.");
        backlog.Subcommands.Add(CreateBacklogBuildCommand(service));
        backlog.Subcommands.Add(CreateBacklogSimpleCommand("validate", "Validate BRD coverage, provenance, dependencies, and lifecycle readiness.", service.Validate));
        backlog.Subcommands.Add(CreateBacklogSimpleCommand("status", "Report high-level backlog lifecycle and source currency.", service.Status));
        backlog.Subcommands.Add(CreateBacklogApproveCommand(service));
        backlog.Subcommands.Add(CreateBacklogStartCommand(service));
        return backlog;
    }

    private static Command CreateBacklogStartCommand(BrdBacklogService service)
    {
        var command = new Command("start", "Start a feature specification from one dependency-ready item in an Active backlog.");
        var workspace = WorkspaceOption(); var format = FormatOption();
        var item = new Option<string>("--item") { Required = true, Description = "Stable HLT item ID." };
        var slug = new Option<string?>("--slug") { Description = "Optional feature directory slug; defaults to the lowercase HLT ID." };
        command.Options.Add(workspace); command.Options.Add(item); command.Options.Add(slug); command.Options.Add(format);
        command.SetAction(parseResult =>
        {
            var selected = GetFormat(parseResult.GetValue(format)); if (selected is null) return 2;
            var result = service.StartFeature(parseResult.GetValue(workspace) ?? Directory.GetCurrentDirectory(),
                parseResult.GetValue(item) ?? string.Empty, parseResult.GetValue(slug));
            RenderFeature(result, selected); return result.ExitCode;
        });
        return command;
    }

    private static Command CreateBacklogBuildCommand(BrdBacklogService service)
    {
        var command = new Command("build", "Build candidate outcomes or record an explicit no-planned-work baseline.");
        var workspace = WorkspaceOption(); var format = FormatOption();
        var mode = new Option<string?>("--mode") { Description = "requirements or no-planned-work; omission preserves a recorded no-work choice." };
        var actor = new Option<string?>("--actor") { Description = "Human recording the no-planned-work choice." };
        command.Options.Add(workspace); command.Options.Add(format); command.Options.Add(mode); command.Options.Add(actor);
        command.SetAction(parse =>
        {
            var selected = GetFormat(parse.GetValue(format)); if (selected is null) return 2;
            var result = service.Build(parse.GetValue(workspace) ?? Directory.GetCurrentDirectory(), parse.GetValue(mode), parse.GetValue(actor));
            RenderBacklog(result, selected); return result.ExitCode;
        });
        return command;
    }

    private static Command CreateBacklogSimpleCommand(string name, string description, Func<string, BrdBacklogResult> action)
    {
        var command = new Command(name, description);
        var workspace = WorkspaceOption(); var format = FormatOption();
        command.Options.Add(workspace); command.Options.Add(format);
        command.SetAction(parseResult =>
        {
            var selected = GetFormat(parseResult.GetValue(format)); if (selected is null) return 2;
            var result = action(parseResult.GetValue(workspace) ?? Directory.GetCurrentDirectory());
            RenderBacklog(result, selected); return result.ExitCode;
        });
        return command;
    }

    private static Command CreateBacklogApproveCommand(BrdBacklogService service)
    {
        var command = new Command("approve", "Record explicit human approval of the current high-level backlog.");
        var workspace = WorkspaceOption(); var format = FormatOption();
        var reviewer = new Option<string>("--reviewer") { Required = true, Description = "Human reviewer identity." };
        var reason = new Option<string>("--reason") { Required = true, Description = "Approval rationale." };
        command.Options.Add(workspace); command.Options.Add(reviewer); command.Options.Add(reason); command.Options.Add(format);
        command.SetAction(parseResult =>
        {
            var selected = GetFormat(parseResult.GetValue(format)); if (selected is null) return 2;
            var result = service.Approve(parseResult.GetValue(workspace) ?? Directory.GetCurrentDirectory(),
                parseResult.GetValue(reviewer) ?? string.Empty, parseResult.GetValue(reason) ?? string.Empty);
            RenderBacklog(result, selected); return result.ExitCode;
        });
        return command;
    }

    private static Command CreateDiscoverCommand(BrdService service)
    {
        var command = new Command(
            "discover",
            "Find possible BRD evidence without treating discovery as current authority.");
        var workspace = WorkspaceOption();
        var format = FormatOption();
        command.Options.Add(workspace);
        command.Options.Add(format);
        command.SetAction(parseResult =>
        {
            var selectedFormat = GetFormat(parseResult.GetValue(format));
            if (selectedFormat is null)
            {
                return 2;
            }

            var result = service.Discover(
                parseResult.GetValue(workspace) ?? Directory.GetCurrentDirectory());
            RenderDiscovery(result, selectedFormat);
            return result.ExitCode;
        });
        return command;
    }

    private static Command CreateInitCommand(BrdService service)
    {
        var command = new Command(
            "init",
            "Create or reconcile the canonical review-required BRD in the workspace authority repository.");
        var workspace = WorkspaceOption();
        var title = new Option<string>("--title")
        {
            Description = "Canonical BRD title.",
            DefaultValueFactory = _ => "Business Requirements Document",
        };
        var format = FormatOption();
        command.Options.Add(workspace);
        command.Options.Add(title);
        command.Options.Add(format);
        command.SetAction(parseResult =>
        {
            var selectedFormat = GetFormat(parseResult.GetValue(format));
            if (selectedFormat is null)
            {
                return 2;
            }

            var result = service.Initialize(
                parseResult.GetValue(workspace) ?? Directory.GetCurrentDirectory(),
                parseResult.GetValue(title) ?? "Business Requirements Document");
            Render(result, selectedFormat);
            return result.ExitCode;
        });
        return command;
    }

    private static Command CreateSimpleCommand(
        string name,
        string description,
        Func<string, BrdResult> action)
    {
        var command = new Command(name, description);
        var workspace = WorkspaceOption();
        var format = FormatOption();
        command.Options.Add(workspace);
        command.Options.Add(format);
        command.SetAction(parseResult =>
        {
            var selectedFormat = GetFormat(parseResult.GetValue(format));
            if (selectedFormat is null)
            {
                return 2;
            }

            var result = action(parseResult.GetValue(workspace) ?? Directory.GetCurrentDirectory());
            Render(result, selectedFormat);
            return result.ExitCode;
        });
        return command;
    }

    private static Command CreateApproveCommand(BrdService service)
    {
        var command = new Command(
            "approve",
            "Record explicit human approval of a valid BRD against current participant evidence.");
        var workspace = WorkspaceOption();
        var reviewer = new Option<string>("--reviewer")
        {
            Description = "Human reviewer identity.",
            Required = true,
        };
        var reason = new Option<string>("--reason")
        {
            Description = "Human approval rationale.",
            Required = true,
        };
        var format = FormatOption();
        command.Options.Add(workspace);
        command.Options.Add(reviewer);
        command.Options.Add(reason);
        command.Options.Add(format);
        command.SetAction(parseResult =>
        {
            var selectedFormat = GetFormat(parseResult.GetValue(format));
            if (selectedFormat is null)
            {
                return 2;
            }

            var result = service.Approve(
                parseResult.GetValue(workspace) ?? Directory.GetCurrentDirectory(),
                parseResult.GetValue(reviewer) ?? string.Empty,
                parseResult.GetValue(reason) ?? string.Empty);
            Render(result, selectedFormat);
            return result.ExitCode;
        });
        return command;
    }

    private static Option<string> WorkspaceOption()
        => new("--workspace")
        {
            Description = "CIS workspace path. Defaults to the current directory.",
            DefaultValueFactory = _ => Directory.GetCurrentDirectory(),
        };

    private static Option<string> FormatOption()
        => new("--format")
        {
            Description = "Output format: human, json, or agent.",
            DefaultValueFactory = _ => "human",
        };

    private static string? GetFormat(string? value)
    {
        var format = (value ?? "human").ToLowerInvariant();
        if (Formats.Contains(format, StringComparer.Ordinal))
        {
            return format;
        }

        Console.Error.WriteLine($"Unsupported format '{format}'. Expected human, json, or agent.");
        return null;
    }

    private static void RenderDiscovery(BrdDiscoveryResult result, string format)
    {
        if (format == "json")
        {
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return;
        }

        if (format == "agent")
        {
            Console.WriteLine(
                $"status={Clean(result.Status)};exitCode={result.ExitCode};candidates={result.Candidates.Count};" +
                $"repositories={result.Baselines.Count}");
            Console.WriteLine($"workspace={Clean(result.WorkspacePath)}");
            Console.WriteLine($"authority={Clean(result.AuthorityRepositoryId)}");
            Console.WriteLine($"canonicalPath={Clean(result.CanonicalPath)}");
            RenderDiscoveryItems(result);
            return;
        }

        Console.WriteLine($"BRD discovery: {result.Status}");
        Console.WriteLine($"Workspace: {result.WorkspacePath ?? string.Empty}");
        Console.WriteLine($"Authority: {result.AuthorityRepositoryId ?? string.Empty}");
        foreach (var candidate in result.Candidates)
        {
            Console.WriteLine(
                $"Candidate: {candidate.Id} {candidate.RepositoryId}/{candidate.Path} " +
                $"[{string.Join(", ", candidate.Signals)}]");
        }

        foreach (var warning in result.Warnings)
        {
            Console.WriteLine($"Warning: {warning}");
        }

        foreach (var error in result.Errors)
        {
            Console.WriteLine($"Error: {error}");
        }
    }

    private static void Render(BrdResult result, string format)
    {
        if (format == "json")
        {
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return;
        }

        if (format == "agent")
        {
            Console.WriteLine(
                $"status={Clean(result.Status)};exitCode={result.ExitCode};applied={result.Applied.ToString().ToLowerInvariant()};" +
                $"effectiveStatus={Clean(result.Validation?.EffectiveStatus)};valid={result.Validation?.Valid.ToString().ToLowerInvariant() ?? "false"};" +
                $"current={result.Validation?.Current.ToString().ToLowerInvariant() ?? "false"}");
            Console.WriteLine($"workspace={Clean(result.WorkspacePath)}");
            Console.WriteLine($"authority={Clean(result.AuthorityRepositoryId)}");
            Console.WriteLine($"canonicalPath={Clean(result.CanonicalPath)}");
            if (result.Discovery is not null)
            {
                RenderDiscoveryItems(result.Discovery);
            }

            foreach (var error in result.Validation?.Errors ?? [])
            {
                Console.WriteLine($"validationError={Clean(error)}");
            }

            foreach (var warning in result.Validation?.Warnings ?? [])
            {
                Console.WriteLine($"validationWarning={Clean(warning)}");
            }

            foreach (var error in result.Errors)
            {
                Console.WriteLine($"error={Clean(error)}");
            }

            return;
        }

        Console.WriteLine($"BRD: {result.Status}");
        Console.WriteLine($"Canonical path: {result.CanonicalPath ?? string.Empty}");
        if (result.Validation is not null)
        {
            Console.WriteLine(
                $"Effective status: {result.Validation.EffectiveStatus}; valid: {result.Validation.Valid}; " +
                $"current: {result.Validation.Current}");
            foreach (var error in result.Validation.Errors)
            {
                Console.WriteLine($"Validation error: {error}");
            }
        }

        foreach (var error in result.Errors)
        {
            Console.WriteLine($"Error: {error}");
        }
    }

    private static void RenderQuestions(BrdQuestionsResult result, string format)
    {
        if (format == "json") { Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions)); return; }
        if (format == "agent")
        {
            Console.WriteLine($"status={Clean(result.Status)};exitCode={result.ExitCode};applied={result.Applied.ToString().ToLowerInvariant()};questions={result.Questions.Count};unanswered={result.UnansweredCount};answerDigest={Clean(result.AnswerDigest)}");
            Console.WriteLine($"workspace={Clean(result.WorkspacePath)}");
            Console.WriteLine($"authority={Clean(result.AuthorityRepositoryId)}");
            Console.WriteLine($"canonicalPath={Clean(result.CanonicalPath)}");
            foreach (var question in result.Questions)
                Console.WriteLine($"question={Clean(question.Id)};ordinal={question.Ordinal};status={Clean(question.Status)};text={Clean(question.Question)};actor={Clean(question.AnsweredBy)};answeredAt={Clean(question.AnsweredAtUtc)}");
            foreach (var error in result.Errors) Console.WriteLine($"error={Clean(error)}");
            return;
        }
        Console.WriteLine($"BRD open questions: {result.Status}; {result.UnansweredCount} unanswered of {result.Questions.Count}");
        foreach (var question in result.Questions)
            Console.WriteLine($"- {question.Id} [{question.Status}] {question.Question}{(question.Answer is null ? string.Empty : " — " + question.Answer)}");
        foreach (var error in result.Errors) Console.WriteLine($"Error: {error}");
    }

    private static int RenderQuestionGuidance(BrdQuestionGuidanceResult result, string format)
    {
        if (format == "json") Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        else if (format == "agent")
        {
            Console.WriteLine($"status={Clean(result.Status)};exitCode={result.ExitCode};questions={result.Questions.Count};unanswered={result.UnansweredCount};suggested={result.SuggestedCount};suggestionStatus={Clean(result.SuggestionStatus)};applied={result.Applied.ToString().ToLowerInvariant()}");
            Console.WriteLine($"workspace={Clean(result.WorkspacePath)}");
            Console.WriteLine($"canonicalPath={Clean(result.CanonicalPath)}");
            Console.WriteLine($"suggestionProvider={Clean(result.SuggestionProvider)};suggestionModel={Clean(result.SuggestionModel)};suggestedAt={Clean(result.SuggestedAtUtc)}");
            foreach (var question in result.Questions)
            {
                Console.WriteLine($"question={Clean(question.Id)};status={Clean(question.Status)};text={Clean(question.Question)};suggestionConfidence={Clean(question.SuggestionConfidence)};suggestedAnswer={Clean(question.SuggestedAnswer)};suggestionReason={Clean(question.SuggestionReason)}");
                foreach (var context in question.Context)
                    Console.WriteLine($"context={Clean(context.Id)};question={Clean(question.Id)};section={Clean(context.Section)};excerpt={Clean(context.Excerpt)}");
            }
            foreach (var error in result.Errors) Console.WriteLine($"error={Clean(error)}");
        }
        else
        {
            Console.WriteLine($"BRD question guidance: {result.Status}; {result.UnansweredCount} unanswered; {result.SuggestedCount} suggestions ({result.SuggestionStatus})");
            foreach (var question in result.Questions)
            {
                Console.WriteLine($"- {question.Id} [{question.Status}] {question.Question}");
                foreach (var context in question.Context) Console.WriteLine($"  - {context.Section}: {context.Excerpt}");
                if (question.SuggestedAnswer is not null) Console.WriteLine($"  Suggested ({question.SuggestionConfidence}): {question.SuggestedAnswer}");
            }
            foreach (var error in result.Errors) Console.WriteLine($"Error: {error}");
        }
        return result.ExitCode;
    }

    private static int RenderReviewDisposition(BrdReviewDispositionResult result, string format)
    {
        if (format == "json") Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        else if (format == "agent")
        {
            var approved = result.Disposition is not null && CisBrdReviewDispositionCodec.IsApproved(result.Disposition);
            Console.WriteLine($"status={Clean(result.Status)};exitCode={result.ExitCode};applied={result.Applied.ToString().ToLowerInvariant()};pending={result.PendingCount};accepted={result.AcceptedCount};rejected={result.RejectedCount};approved={approved.ToString().ToLowerInvariant()}");
            Console.WriteLine($"workspace={Clean(result.WorkspacePath)}");
            Console.WriteLine($"canonicalPath={Clean(result.CanonicalPath)}");
            foreach (var finding in result.Disposition?.Findings ?? [])
                Console.WriteLine($"finding={Clean(finding.Id)};severity={Clean(finding.Severity)};decision={Clean(finding.Decision)};category={Clean(finding.Category)};location={Clean(finding.Location)};observation={Clean(finding.Observation)};recommendation={Clean(finding.Recommendation)};approvedRecommendation={Clean(finding.ApprovedRecommendation)};actor={Clean(finding.DecidedBy)};reason={Clean(finding.Rationale)}");
            foreach (var error in result.Errors) Console.WriteLine($"error={Clean(error)}");
        }
        else
        {
            Console.WriteLine($"BRD review recommendations: {result.Status}; {result.PendingCount} pending, {result.AcceptedCount} accepted, {result.RejectedCount} rejected");
            foreach (var finding in result.Disposition?.Findings ?? [])
                Console.WriteLine($"- {finding.Id} [{finding.Severity}] {finding.Decision}: {(finding.Decision == "accepted" ? CisBrdReviewDispositionCodec.EffectiveRecommendation(finding) : finding.Recommendation)}");
            foreach (var error in result.Errors) Console.WriteLine($"Error: {error}");
        }
        return result.ExitCode;
    }

    private static int RenderReviewFreshness(CisBrdReviewFreshness result, string format)
    {
        if (format == "json") Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        else if (format == "agent")
        {
            Console.WriteLine($"status={Clean(result.Status)};exitCode={result.ExitCode};compatible={result.Compatible.ToString().ToLowerInvariant()};runId={Clean(result.ReviewRunId)}");
            Console.WriteLine($"workspace={Clean(result.WorkspacePath)}");
            Console.WriteLine($"canonicalPath={Clean(result.CanonicalPath)}");
            Console.WriteLine($"reviewedSha256={Clean(result.ReviewedSha256)};currentSha256={Clean(result.CurrentSha256)}");
            foreach (var error in result.Errors) Console.WriteLine($"error={Clean(error)}");
        }
        else
        {
            Console.WriteLine($"BRD review freshness: {result.Status}; compatible: {result.Compatible}");
            Console.WriteLine($"Review run: {result.ReviewRunId ?? string.Empty}");
            Console.WriteLine($"Canonical path: {result.CanonicalPath ?? string.Empty}");
            foreach (var error in result.Errors) Console.WriteLine($"Error: {error}");
        }
        return result.ExitCode;
    }

    private static void RenderBacklog(BrdBacklogResult result, string format)
    {
        if (format == "json") { Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions)); return; }
        if (format == "agent")
        {
            Console.WriteLine($"status={Clean(result.Status)};exitCode={result.ExitCode};applied={result.Applied.ToString().ToLowerInvariant()};items={result.Items.Count};effectiveStatus={Clean(result.Validation?.EffectiveStatus)};valid={result.Validation?.Valid.ToString().ToLowerInvariant() ?? "false"};current={result.Validation?.Current.ToString().ToLowerInvariant() ?? "false"}");
            Console.WriteLine($"workspace={Clean(result.WorkspacePath)}"); Console.WriteLine($"authority={Clean(result.AuthorityRepositoryId)}"); Console.WriteLine($"canonicalPath={Clean(result.RelativePath)}");
            if (result.Validation?.Current != false)
                foreach (var item in result.Items) Console.WriteLine($"item={Clean(item.Id)};requirement={Clean(item.RequirementId)};repositories={Clean(string.Join(',', item.Repositories))};frontendTypes={Clean(string.Join(',', item.FrontendTypes))};dependsOn={Clean(string.Join(',', item.DependsOn))};featureSpec={Clean(item.FeatureSpecification)};outcome={Clean(item.Outcome)}");
            foreach (var error in result.Validation?.Errors ?? []) Console.WriteLine($"validationError={Clean(error)}");
            foreach (var warning in result.Validation?.Warnings ?? []) Console.WriteLine($"validationWarning={Clean(warning)}");
            foreach (var error in result.Errors) Console.WriteLine($"error={Clean(error)}");
            return;
        }
        Console.WriteLine($"BRD high-level backlog: {result.Status}; items: {result.Items.Count}");
        Console.WriteLine($"Effective status: {result.Validation?.EffectiveStatus ?? string.Empty}");
        if (result.Validation?.Current != false)
            foreach (var item in result.Items) Console.WriteLine($"- {item.Id} [{item.RequirementId}] {item.Outcome}");
        foreach (var error in result.Validation?.Errors ?? []) Console.WriteLine($"Validation error: {error}");
        foreach (var warning in result.Validation?.Warnings ?? []) Console.WriteLine($"Warning: {warning}");
        foreach (var error in result.Errors) Console.WriteLine($"Error: {error}");
    }

    private static void RenderFeature(BrdFeatureSpecificationResult result, string format)
    {
        if (format == "json") { Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions)); return; }
        if (format == "agent")
        {
            Console.WriteLine($"status={Clean(result.Status)};exitCode={result.ExitCode};applied={result.Applied.ToString().ToLowerInvariant()};item={Clean(result.ItemId)};requirement={Clean(result.RequirementId)}");
            Console.WriteLine($"workspace={Clean(result.WorkspacePath)}");
            Console.WriteLine($"authority={Clean(result.AuthorityRepositoryId)}");
            Console.WriteLine($"canonicalPath={Clean(result.RelativePath)}");
            foreach (var error in result.Errors) Console.WriteLine($"error={Clean(error)}");
            return;
        }
        Console.WriteLine($"Feature specification: {result.Status}");
        Console.WriteLine($"Item: {result.ItemId ?? string.Empty}; requirement: {result.RequirementId ?? string.Empty}");
        Console.WriteLine($"Canonical path: {result.RelativePath ?? string.Empty}");
        foreach (var error in result.Errors) Console.WriteLine($"Error: {error}");
    }

    private static void RenderFeatureLifecycle(BrdFeatureResult result, string format)
    {
        if (format == "json") { Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions)); return; }
        if (format == "agent")
        {
            Console.WriteLine($"status={Clean(result.Status)};exitCode={result.ExitCode};applied={result.Applied.ToString().ToLowerInvariant()};item={Clean(result.ItemId)};requirement={Clean(result.RequirementId)};effectiveStatus={Clean(result.Validation?.EffectiveStatus)};valid={result.Validation?.Valid.ToString().ToLowerInvariant() ?? "false"};current={result.Validation?.Current.ToString().ToLowerInvariant() ?? "false"}");
            Console.WriteLine($"workspace={Clean(result.WorkspacePath)}");
            Console.WriteLine($"authority={Clean(result.AuthorityRepositoryId)}");
            Console.WriteLine($"canonicalPath={Clean(result.RelativePath)}");
            foreach (var error in result.Validation?.Errors ?? []) Console.WriteLine($"validationError={Clean(error)}");
            foreach (var warning in result.Validation?.Warnings ?? []) Console.WriteLine($"validationWarning={Clean(warning)}");
            foreach (var error in result.Errors) Console.WriteLine($"error={Clean(error)}");
            return;
        }
        Console.WriteLine($"BRD feature specification: {result.Status}");
        Console.WriteLine($"Item: {result.ItemId ?? string.Empty}; requirement: {result.RequirementId ?? string.Empty}");
        Console.WriteLine($"Canonical path: {result.RelativePath ?? string.Empty}");
        Console.WriteLine($"Effective status: {result.Validation?.EffectiveStatus ?? string.Empty}");
        foreach (var error in result.Validation?.Errors ?? []) Console.WriteLine($"Validation error: {error}");
        foreach (var warning in result.Validation?.Warnings ?? []) Console.WriteLine($"Warning: {warning}");
        foreach (var error in result.Errors) Console.WriteLine($"Error: {error}");
    }

    private static void RenderDiscoveryItems(BrdDiscoveryResult result)
    {
        foreach (var baseline in result.Baselines)
        {
            Console.WriteLine(
                $"baseline={Clean(baseline.RepositoryId)};role={baseline.Role};buildId={Clean(baseline.BuildId)};" +
                $"head={Clean(baseline.Head)};dirty={baseline.Dirty.ToString().ToLowerInvariant()};" +
                $"freshness={baseline.Freshness};graphStatus={baseline.GraphStatus}");
        }

        foreach (var candidate in result.Candidates)
        {
            Console.WriteLine(
                $"candidate={candidate.Id};kind={candidate.Kind};repository={Clean(candidate.RepositoryId)};path={Clean(candidate.Path)};" +
                $"hash={candidate.ContentHash};canonical={candidate.Canonical.ToString().ToLowerInvariant()};" +
                $"signals={Clean(string.Join(',', candidate.Signals))}");
        }

        foreach (var warning in result.Warnings)
        {
            Console.WriteLine($"warning={Clean(warning)}");
        }

        foreach (var error in result.Errors)
        {
            Console.WriteLine($"error={Clean(error)}");
        }
    }

    private static string Clean(string? value)
        => (value ?? string.Empty).Replace(';', ',').Replace('\r', ' ').Replace('\n', ' ').Trim();
}

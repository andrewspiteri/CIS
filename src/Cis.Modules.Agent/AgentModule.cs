using System.CommandLine;
using System.Text.Json;
using Cis.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Cis.Modules.Agent;

public sealed class AgentModule : ICisModule
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public string Name => "agent";
    public string Description => "Prepare and execute bounded provider-neutral agent tasks and reconcile structured results without granting lifecycle authority.";

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton(serviceProvider => new AgentService(
            serviceProvider.GetRequiredService<ICisRepositoryContextResolver>(),
            serviceProvider.GetServices<ICisAgentProvider>(),
            serviceProvider.GetService<ICisWorkspaceRegistry>(),
            sourceEvidenceRegistrars: serviceProvider.GetServices<ICisSourceEvidenceRegistrar>(),
            sourceEvidenceReconciler: serviceProvider.GetService<ICisBrdSourceEvidenceReconciler>(),
            productDefinitionAuthorities: serviceProvider.GetServices<ICisProductDefinitionAuthority>()));
        services.AddSingleton<AgentEvidenceService>();
        services.AddSingleton<ICisRepositoryDoctorCheck, AgentProviderDoctorCheck>();
    }

    public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services)
    {
        var service = services.GetRequiredService<AgentService>();
        var root = new Command(Name, Description);
        root.Subcommands.Add(Simple("providers", "List discovered envelope and direct-execution providers.", service.Providers));
        root.Subcommands.Add(ProviderCommands(service));
        root.Subcommands.Add(Prepare(service));
        root.Subcommands.Add(Run(service));
        root.Subcommands.Add(Author(service));
        root.Subcommands.Add(Incorporate(service));
        root.Subcommands.Add(Revise(service));
        root.Subcommands.Add(Review(service));
        root.Subcommands.Add(Runs(service, services.GetService<ICisTokenSavingsCollector>()));
        root.Subcommands.Add(Show(service));
        root.Subcommands.Add(Cancel(service));
        root.Subcommands.Add(Recover(service));
        root.Subcommands.Add(Revalidate(service));
        root.Subcommands.Add(Resume(service));
        root.Subcommands.Add(Import(service));
        root.Subcommands.Add(Simple("status", "Report envelopes, runs, and imported results.", service.Status));
        root.Subcommands.Add(Evidence(services.GetRequiredService<AgentEvidenceService>()));
        commands.Add(root);
    }

    private static Command ProviderCommands(AgentService service)
    {
        var provider = new Command("provider", "Inspect or authenticate one agent provider.");
        var diagnose = new Command("diagnose", "Diagnose executable, version, authentication, transport, and capability availability.");
        var id = new Argument<string>("provider"); var repo = Repo(); var format = Format();
        diagnose.Arguments.Add(id); diagnose.Options.Add(repo); diagnose.Options.Add(format);
        diagnose.SetAction(result => Render(service.Diagnose(result.GetValue(repo)!, result.GetValue(id)!), result.GetValue(format)!));
        provider.Subcommands.Add(diagnose);

        var authenticate = new Command("authenticate", "Start provider-native authentication without exposing credentials to CIS.");
        var authenticateId = new Argument<string>("provider");
        var method = new Option<string?>("--method") { Description = "Provider-native method, such as browser or device." };
        var timeout = new Option<int>("--timeout-seconds") { DefaultValueFactory = _ => 600 };
        var authenticateRepo = Repo(); var authenticateFormat = Format();
        authenticate.Arguments.Add(authenticateId); authenticate.Options.Add(method); authenticate.Options.Add(timeout);
        authenticate.Options.Add(authenticateRepo); authenticate.Options.Add(authenticateFormat);
        authenticate.SetAction(result =>
        {
            var outputFormat = result.GetValue(authenticateFormat)!;
            return Render(ExecuteForeground((cancellationToken, progress) => service.Authenticate(
                result.GetValue(authenticateRepo)!, result.GetValue(authenticateId)!, result.GetValue(method),
                result.GetValue(timeout), cancellationToken, progress), outputFormat), outputFormat);
        });
        provider.Subcommands.Add(authenticate); return provider;
    }

    private static Command Prepare(AgentService service)
    {
        var command = new Command("prepare", "Prepare or refresh a digest-bound task envelope.");
        var change = new Argument<string>("change-id"); var task = new Argument<string>("task-id");
        var provider = new Option<string>("--provider") { DefaultValueFactory = _ => "portable" };
        var repo = Repo(); var format = Format(); command.Arguments.Add(change); command.Arguments.Add(task);
        command.Options.Add(provider); command.Options.Add(repo); command.Options.Add(format);
        command.SetAction(result => Render(service.Prepare(result.GetValue(repo)!, result.GetValue(change)!,
            result.GetValue(task)!, result.GetValue(provider)!), result.GetValue(format)!)); return command;
    }

    private static Command Run(AgentService service)
    {
        var command = new Command("run", "Run an eligible task through a discovered provider in the foreground.");
        var change = new Argument<string>("change-id"); var task = new Argument<string>("task-id");
        var provider = new Option<string>("--provider") { Required = true };
        var mode = new Option<string>("--mode") { Required = true };
        var permission = new Option<string>("--permission") { Required = true };
        var target = new Option<string?>("--target"); var transport = new Option<string?>("--transport");
        var timeout = new Option<int>("--timeout-seconds") { DefaultValueFactory = _ => 3_600 };
        var approve = new Option<bool>("--approve-requests") { Description = "Pre-authorize supported provider requests that remain inside the declared permission ceiling." };
        var actor = new Option<string>("--actor") { Required = true }; var repo = Repo(); var format = Format();
        command.Arguments.Add(change); command.Arguments.Add(task);
        foreach (var option in new Option[] { provider, mode, permission, target, transport, timeout, approve, actor, repo, format }) command.Options.Add(option);
        command.SetAction(result =>
        {
            var outputFormat = result.GetValue(format)!;
            return Render(ExecuteForeground((cancellationToken, progress) => service.Run(
                result.GetValue(repo)!, result.GetValue(change)!, result.GetValue(task)!, result.GetValue(provider)!,
                result.GetValue(mode)!, result.GetValue(permission)!, result.GetValue(target), result.GetValue(transport),
                result.GetValue(timeout), result.GetValue(approve), result.GetValue(actor)!, cancellationToken, progress), outputFormat), outputFormat);
        });
        return command;
    }

    private static Command Runs(AgentService service, ICisTokenSavingsCollector? savingsCollector)
    {
        var command = new Command("runs", "List durable local agent runs.");
        var change = new Option<string?>("--change"); var task = new Option<string?>("--task"); var status = new Option<string?>("--status");
        var summary = new Option<bool>("--summary") { Description = "Return bounded list projection fields rather than complete run manifests." };
        var limit = new Option<int>("--limit") { DefaultValueFactory = _ => 10, Description = "Maximum summary rows. Ignored without --summary." };
        var latestPerTask = new Option<bool>("--latest-per-task") { Description = "Return only the newest summary row for each change/task pair." };
        var repo = Repo(); var format = Format(); command.Options.Add(change); command.Options.Add(task); command.Options.Add(status); command.Options.Add(summary); command.Options.Add(limit); command.Options.Add(latestPerTask); command.Options.Add(repo); command.Options.Add(format);
        command.SetAction(result =>
        {
            var summarize = result.GetValue(summary);
            var selectedLimit = result.GetValue(limit);
            if (summarize && selectedLimit is < 1 or > 100) return 2;
            var output = service.Runs(result.GetValue(repo)!, result.GetValue(change), result.GetValue(task), result.GetValue(status),
                summarize ? selectedLimit : null, summarize && result.GetValue(latestPerTask));
            var outputFormat = result.GetValue(format)!;
            if (summarize && outputFormat == "json" && output.ExitCode == 0 && savingsCollector is not null)
            {
                var baseline = Math.Max(1, (int)Math.Ceiling(JsonSerializer.Serialize(output, JsonOptions).Length / 4d));
                savingsCollector.Add(new CisTokenSavingsCandidate(baseline, null,
                    "deterministic full agent-run manifests versus bounded --summary projection", "high"));
            }
            return Render(output, outputFormat, summarizeRuns: summarize);
        });
        return command;
    }

    private static Command Author(AgentService service)
    {
        var group = new Command("author", "Draft canonical pre-change documents from explicitly selected reference evidence.");
        var command = new Command("brd", "Draft the Review Required business requirements document without granting approval authority.");
        var reference = new Option<string[]>("--reference")
        {
            Required = true,
            Arity = ArgumentArity.OneOrMore,
            AllowMultipleArgumentsPerToken = true,
            Description = "One or more explicitly selected text, Word Open XML (.docx), or initialized repository references."
        };
        var provider = new Option<string>("--provider") { Required = true };
        var transport = new Option<string?>("--transport");
        var timeout = new Option<int>("--timeout-seconds") { DefaultValueFactory = _ => 3_600 };
        var approve = new Option<bool>("--approve-requests")
        {
            Description = "Pre-authorize supported provider requests that remain inside the isolated authoring workspace."
        };
        var actor = new Option<string>("--actor") { Required = true };
        var repo = Repo(); var format = Format();
        foreach (var option in new Option[] { reference, provider, transport, timeout, approve, actor, repo, format })
            command.Options.Add(option);
        command.SetAction(result =>
        {
            var outputFormat = result.GetValue(format)!;
            return Render(ExecuteForeground((cancellationToken, progress) => service.AuthorBrd(
                result.GetValue(repo)!, result.GetValue(reference) ?? [], result.GetValue(provider)!,
                result.GetValue(transport), result.GetValue(timeout), result.GetValue(approve),
                result.GetValue(actor)!, cancellationToken, progress), outputFormat), outputFormat);
        });
        group.Subcommands.Add(command);

        var feature = new Command("feature", "Draft one started high-level feature specification from current governed product and technical evidence.");
        var item = new Option<string>("--item") { Required = true, Description = "Started high-level backlog item identity, for example HLT-FR-001." };
        var featureProvider = new Option<string>("--provider") { Required = true };
        var featureTransport = new Option<string?>("--transport");
        var featureTimeout = new Option<int>("--timeout-seconds") { DefaultValueFactory = _ => 3_600 };
        var featureApprove = new Option<bool>("--approve-requests")
        {
            Description = "Pre-authorize supported provider requests that remain inside the isolated one-file feature workspace."
        };
        var featureActor = new Option<string>("--actor") { Required = true };
        var featureRepo = Repo(); var featureFormat = Format();
        foreach (var option in new Option[] { item, featureProvider, featureTransport, featureTimeout, featureApprove, featureActor, featureRepo, featureFormat })
            feature.Options.Add(option);
        feature.SetAction(result =>
        {
            var outputFormat = result.GetValue(featureFormat)!;
            return Render(ExecuteForeground((cancellationToken, progress) => service.AuthorFeature(
                result.GetValue(featureRepo)!, result.GetValue(item)!, result.GetValue(featureProvider)!,
                result.GetValue(featureTransport), result.GetValue(featureTimeout), result.GetValue(featureApprove),
                result.GetValue(featureActor)!, cancellationToken, progress), outputFormat), outputFormat);
        });
        group.Subcommands.Add(feature);
        return group;
    }

    private static Command Incorporate(AgentService service)
    {
        var group = new Command("incorporate", "Apply human-answer evidence to a bounded canonical document through an isolated agent run.");
        var command = new Command("brd-questions", "Incorporate every answered BRD question into the relevant business-requirement sections before independent review.");
        var provider = new Option<string>("--provider") { Required = true };
        var transport = new Option<string?>("--transport");
        var timeout = new Option<int>("--timeout-seconds") { DefaultValueFactory = _ => 3_600 };
        var approve = new Option<bool>("--approve-requests")
        {
            Description = "Pre-authorize supported provider requests within the isolated one-file incorporation ceiling."
        };
        var actor = new Option<string>("--actor") { Required = true };
        var repo = Repo(); var format = Format();
        foreach (var option in new Option[] { provider, transport, timeout, approve, actor, repo, format })
            command.Options.Add(option);
        command.SetAction(result =>
        {
            var outputFormat = result.GetValue(format)!;
            return Render(ExecuteForeground((cancellationToken, progress) => service.IncorporateBrdQuestions(
                result.GetValue(repo)!, result.GetValue(provider)!, result.GetValue(transport),
                result.GetValue(timeout), result.GetValue(approve), result.GetValue(actor)!,
                cancellationToken, progress), outputFormat), outputFormat);
        });
        group.Subcommands.Add(command);
        return group;
    }

    private static Command Review(AgentService service)
    {
        var group = new Command("review", "Run bounded read-only independent agent reviews.");
        var command = new Command("brd", "Review the canonical BRD through a different provider and retain structured advisory findings.");
        var provider = new Option<string>("--provider") { Required = true };
        var transport = new Option<string?>("--transport");
        var timeout = new Option<int>("--timeout-seconds") { DefaultValueFactory = _ => 3_600 };
        var includeEvidence = new Option<bool>("--include-authoring-evidence")
        {
            Description = "Include the latest digest-bound extracted reference evidence from the successful BRD authoring run."
        };
        var actor = new Option<string>("--actor") { Required = true };
        var repo = Repo(); var format = Format();
        foreach (var option in new Option[] { provider, transport, timeout, includeEvidence, actor, repo, format })
            command.Options.Add(option);
        command.SetAction(result =>
        {
            var outputFormat = result.GetValue(format)!;
            return Render(ExecuteForeground((cancellationToken, progress) => service.ReviewBrd(
                result.GetValue(repo)!, result.GetValue(provider)!, result.GetValue(transport),
                result.GetValue(timeout), result.GetValue(includeEvidence), result.GetValue(actor)!,
                cancellationToken, progress), outputFormat), outputFormat);
        });
        group.Subcommands.Add(command);
        return group;
    }

    private static Command Revise(AgentService service)
    {
        var group = new Command("revise", "Apply a human-approved bounded recommendation set through an isolated agent run.");
        var command = new Command("brd", "Apply exact approved BRD review recommendations without granting approval authority.");
        var review = new Option<string>("--review") { Required = true, Description = "Source BRD review run ID." };
        var provider = new Option<string>("--provider") { Required = true };
        var transport = new Option<string?>("--transport");
        var timeout = new Option<int>("--timeout-seconds") { DefaultValueFactory = _ => 3_600 };
        var approve = new Option<bool>("--approve-requests")
        {
            Description = "Pre-authorize supported provider requests within the isolated one-file revision ceiling."
        };
        var actor = new Option<string>("--actor") { Required = true }; var repo = Repo(); var format = Format();
        foreach (var option in new Option[] { review, provider, transport, timeout, approve, actor, repo, format })
            command.Options.Add(option);
        command.SetAction(result =>
        {
            var outputFormat = result.GetValue(format)!;
            return Render(ExecuteForeground((cancellationToken, progress) => service.ReviseBrd(
                result.GetValue(repo)!, result.GetValue(review)!, result.GetValue(provider)!, result.GetValue(transport),
                result.GetValue(timeout), result.GetValue(approve), result.GetValue(actor)!, cancellationToken, progress), outputFormat), outputFormat);
        });
        group.Subcommands.Add(command);
        return group;
    }

    private static Command Show(AgentService service)
    {
        var command = new Command("show", "Show one run, its append-only events, permissions, result, and artifacts.");
        var run = new Argument<string>("run-id");
        var summary = new Option<bool>("--summary") { Description = "Omit append-only events from structured output when only run state and result evidence are needed." };
        var repo = Repo(); var format = Format(); command.Arguments.Add(run); command.Options.Add(summary); command.Options.Add(repo); command.Options.Add(format);
        command.SetAction(result => Render(service.Show(result.GetValue(repo)!, result.GetValue(run)!),
            result.GetValue(format)!, result.GetValue(summary))); return command;
    }

    private static Command Cancel(AgentService service)
    {
        var command = new Command("cancel", "Cancel the exact owned process tree of an active local run.");
        var run = new Argument<string>("run-id"); var actor = Required("--actor"); var reason = Required("--reason"); var repo = Repo(); var format = Format();
        command.Arguments.Add(run); command.Options.Add(actor); command.Options.Add(reason); command.Options.Add(repo); command.Options.Add(format);
        command.SetAction(result => Render(service.Cancel(result.GetValue(repo)!, result.GetValue(run)!, result.GetValue(actor)!, result.GetValue(reason)!), result.GetValue(format)!)); return command;
    }

    private static Command Resume(AgentService service)
    {
        var command = new Command("resume", "Create another retained attempt for a terminal run when its provider supports continuation.");
        var run = new Argument<string>("run-id"); var message = new Option<string?>("--message"); var actor = Required("--actor"); var reason = Required("--reason");
        var approve = new Option<bool>("--approve-requests"); var repo = Repo(); var format = Format(); command.Arguments.Add(run);
        command.Options.Add(message); command.Options.Add(actor); command.Options.Add(reason); command.Options.Add(approve); command.Options.Add(repo); command.Options.Add(format);
        command.SetAction(result =>
        {
            var outputFormat = result.GetValue(format)!;
            return Render(ExecuteForeground((cancellationToken, progress) => service.Resume(
                result.GetValue(repo)!, result.GetValue(run)!, result.GetValue(message), result.GetValue(actor)!,
                result.GetValue(reason)!, result.GetValue(approve), cancellationToken, progress), outputFormat), outputFormat);
        });
        return command;
    }

    private static Command Recover(AgentService service)
    {
        var command = new Command("recover", "Mark a proven orphaned non-terminal run interrupted and release only its matching stale task lock.");
        var run = new Argument<string>("run-id"); var actor = Required("--actor"); var reason = Required("--reason"); var repo = Repo(); var format = Format();
        command.Arguments.Add(run); command.Options.Add(actor); command.Options.Add(reason); command.Options.Add(repo); command.Options.Add(format);
        command.SetAction(result => Render(service.Recover(result.GetValue(repo)!, result.GetValue(run)!, result.GetValue(actor)!, result.GetValue(reason)!), result.GetValue(format)!));
        return command;
    }

    private static Command Revalidate(AgentService service)
    {
        var command = new Command("revalidate", "Revalidate a retained read-only BRD review that was rejected solely because legacy CIS could not retain a valid oversized Claude telemetry event.");
        var run = new Argument<string>("run-id"); var actor = Required("--actor"); var reason = Required("--reason"); var repo = Repo(); var format = Format();
        command.Arguments.Add(run); command.Options.Add(actor); command.Options.Add(reason); command.Options.Add(repo); command.Options.Add(format);
        command.SetAction(result => Render(service.Revalidate(result.GetValue(repo)!, result.GetValue(run)!,
            result.GetValue(actor)!, result.GetValue(reason)!), result.GetValue(format)!));
        return command;
    }

    private static Command Import(AgentService service)
    {
        var command = new Command("import-result", "Import a structured result against an unchanged envelope without changing task lifecycle.");
        var envelope = new Argument<string>("envelope"); var resultPath = new Option<string>("--result") { Required = true };
        var repo = Repo(); var format = Format(); command.Arguments.Add(envelope); command.Options.Add(resultPath); command.Options.Add(repo); command.Options.Add(format);
        command.SetAction(result => Render(service.ImportResult(result.GetValue(repo)!, result.GetValue(envelope)!, result.GetValue(resultPath)!), result.GetValue(format)!)); return command;
    }

    private static Command Evidence(AgentEvidenceService service)
    {
        var group = new Command("evidence", "Validate CIS tooling evidence for changed files.");
        var validate = new Command("validate", "Validate an evidence or handoff document.");
        var evidence = new Option<string>("--evidence") { Required = true };
        var changed = new Option<string[]>("--changed-file") { Arity = ArgumentArity.ZeroOrMore, AllowMultipleArgumentsPerToken = true };
        var strict = new Option<bool>("--strict"); var repo = Repo(); var format = Format();
        validate.Options.Add(evidence); validate.Options.Add(changed); validate.Options.Add(strict); validate.Options.Add(repo); validate.Options.Add(format);
        validate.SetAction(result => RenderEvidence(service.Validate(result.GetValue(repo)!, result.GetValue(evidence)!, result.GetValue(changed) ?? [], result.GetValue(strict)), result.GetValue(format)!));
        group.Subcommands.Add(validate); return group;
    }

    private static Command Simple(string name, string description, Func<string, AgentResult> action)
    {
        var command = new Command(name, description); var repo = Repo(); var format = Format(); command.Options.Add(repo); command.Options.Add(format);
        command.SetAction(result => Render(action(result.GetValue(repo)!), result.GetValue(format)!)); return command;
    }

    private static int Render(AgentResult result, string format, bool summarizeRun = false, bool summarizeRuns = false)
    {
        if (format == "json") Console.WriteLine(JsonSerializer.Serialize(
            summarizeRun ? SummarizeRun(result) : summarizeRuns ? SummarizeRuns(result) : result, JsonOptions));
        else if (format == "agent")
        {
            Console.WriteLine($"status={result.Status};exitCode={result.ExitCode};providers={result.Providers.Count};runs={result.Runs.Count};imports={result.Imports.Count};applied={result.Applied.ToString().ToLowerInvariant()}");
            if (result.Envelope is not null) Console.WriteLine($"envelope={result.Envelope.Id}");
            if (result.Run is not null) Console.WriteLine($"run={result.Run.Manifest.RunId};attempt={result.Run.Manifest.Attempt};runStatus={result.Run.Manifest.Status};provider={result.Run.Manifest.Provider}");
            if (result.Run?.Result?.Review is { } review)
            {
                Console.WriteLine($"brdReview={review.Recommendation};findings={review.Findings.Count};strengths={review.Strengths.Count}");
                Console.WriteLine($"reviewPath=.cis/local/agents/runs/{result.Run.Manifest.RunId}/brd-review.md");
            }
            foreach (var diagnosis in result.Diagnoses) Console.WriteLine($"diagnosis={diagnosis.Provider};status={diagnosis.Status};available={diagnosis.Available.ToString().ToLowerInvariant()};executable={Safe(diagnosis.Executable)};version={Safe(diagnosis.Version)}");
            foreach (var diagnostic in result.Diagnostics) Console.WriteLine($"diagnostic={Safe(diagnostic)}");
        }
        else if (format == "human")
        {
            Console.WriteLine($"Agent: {result.Status}; providers={result.Providers.Count}; runs={result.Runs.Count}; imports={result.Imports.Count}");
            foreach (var provider in result.Providers) Console.WriteLine($"- {provider.Id}: {(provider.DirectExecution ? "direct" : "envelope")} [{string.Join(", ", provider.Transports)}]");
            foreach (var diagnosis in result.Diagnoses) Console.WriteLine($"Provider {diagnosis.Provider}: {diagnosis.Status} ({diagnosis.Version ?? "version unavailable"}); executable={diagnosis.Executable ?? "unresolved"}");
            foreach (var run in result.Runs) Console.WriteLine($"Run {run.RunId}: {run.Status}; {run.ChangeId}/{run.TaskId}; {run.Provider}/{run.Transport}; attempt {run.Attempt}");
            if (result.Run is not null) foreach (var item in result.Run.Events.TakeLast(20)) Console.WriteLine($"  {item.Sequence}. [{item.Kind}] {item.Message}");
            if (result.Run?.Result?.Review is { } review)
                Console.WriteLine($"BRD review: {review.Recommendation}; {review.Findings.Count} findings; .cis/local/agents/runs/{result.Run.Manifest.RunId}/brd-review.md");
            foreach (var diagnostic in result.Diagnostics) Console.WriteLine(diagnostic);
        }
        else return 2;
        return result.ExitCode;
    }

    private static object SummarizeRun(AgentResult result) => new
    {
        result.Status,
        result.RepositoryPath,
        result.Envelope,
        result.Providers,
        result.Diagnoses,
        result.Imports,
        result.Runs,
        Run = result.Run is null ? null : new
        {
            result.Run.Manifest,
            result.Run.Result,
            result.Run.Permissions,
            result.Run.Artifacts,
            EventKinds = result.Run.Events.Select(item => item.Kind).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
        },
        result.Diagnostics,
        result.Applied,
        result.ExitCode,
    };

    private static object SummarizeRuns(AgentResult result) => new
    {
        result.Status,
        result.RepositoryPath,
        Runs = result.Runs.Select(item => new
        {
            item.RunId,
            item.Attempt,
            item.ChangeId,
            item.TaskId,
            item.Provider,
            item.TaskDigest,
            item.UpdatedAtUtc,
            item.CompletedAtUtc,
            item.Status,
            item.FailureKind,
        }).ToArray(),
        result.Diagnostics,
        result.Applied,
        result.ExitCode,
    };

    private static int RenderEvidence(AgentEvidenceResult result, string format)
    {
        if (format == "json") Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        else if (format == "agent") { Console.WriteLine($"status={result.Status};exitCode={result.ExitCode};requirements={result.Requirements.Count};strict={result.Strict.ToString().ToLowerInvariant()}"); foreach (var diagnostic in result.Diagnostics) Console.WriteLine($"diagnostic={Safe(diagnostic)}"); }
        else if (format == "human") { Console.WriteLine($"Agent tooling evidence: {result.Status}"); foreach (var requirement in result.Requirements) Console.WriteLine($"- required: {requirement}"); foreach (var diagnostic in result.Diagnostics) Console.WriteLine(diagnostic); }
        else return 2; return result.ExitCode;
    }

    private static AgentResult ExecuteForeground(
        Func<CancellationToken, Action<CisAgentProviderEvent>, AgentResult> execute,
        string format)
    {
        using var source = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            source.Cancel();
        };
        Console.CancelKeyPress += handler;
        try
        {
            return execute(source.Token, item => RenderProgress(item, format));
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }
    }

    private static void RenderProgress(CisAgentProviderEvent item, string format)
    {
        // High-frequency provider heartbeats remain in the durable run log but do not flood the terminal or VS Code UI.
        if (item.Kind == "provider-heartbeat") return;
        if (item.Kind == "provider-event" && !IsTerminalMilestone(item.ProviderEventType)) return;
        if (format == "json")
            Console.Error.WriteLine(JsonSerializer.Serialize(new { type = "agent-event", @event = item }, JsonOptions));
        else if (format == "agent")
            Console.Error.WriteLine($"agent-event={Safe(item.Kind)};providerType={Safe(item.ProviderEventType)};message={Safe(item.Message)}");
        else
            Console.Error.WriteLine($"[{item.Kind}] {item.Message}");
    }

    private static bool IsTerminalMilestone(string? providerEventType)
    {
        var value = providerEventType ?? string.Empty;
        return value.Equals("result", StringComparison.OrdinalIgnoreCase)
            || value.Equals("turn/completed", StringComparison.OrdinalIgnoreCase)
            || value.Contains("error", StringComparison.OrdinalIgnoreCase)
            || value.Contains("fail", StringComparison.OrdinalIgnoreCase);
    }

    private static string Safe(string? value) => (value ?? string.Empty).Replace(';', ',').Replace('\r', ' ').Replace('\n', ' ').Trim();
    private static Option<string> Repo() => new("--repo") { DefaultValueFactory = _ => Directory.GetCurrentDirectory() };
    private static Option<string> Format() => new("--format") { DefaultValueFactory = _ => "human" };
    private static Option<string> Required(string name) => new(name) { Required = true };
}

using System.CommandLine;
using System.Text.Json;
using Cis.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Cis.Modules.Feedback;

public sealed class FeedbackModule : ICisModule
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public string Name => "feedback";

    public string Description => "Inspect local CIS tool usage, estimated context savings, and improvement opportunities.";

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<TokenSavingsCollector>();
        services.AddSingleton<ICisTokenSavingsCollector>(provider => provider.GetRequiredService<TokenSavingsCollector>());
        services.AddSingleton<ToolUsageStore>();
        services.AddSingleton<ICisToolUsageRecorder>(provider => provider.GetRequiredService<ToolUsageStore>());
        services.AddSingleton<FeedbackService>();
        services.AddSingleton<ICisRepositoryDoctorCheck, FeedbackDoctorCheck>();
    }

    public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services)
    {
        var service = services.GetRequiredService<FeedbackService>();
        var store = services.GetRequiredService<ToolUsageStore>();
        var feedback = new Command(Name, Description);
        feedback.Subcommands.Add(CreateSummaryCommand(service));
        feedback.Subcommands.Add(CreateUsageCommand(store));
        feedback.Subcommands.Add(CreateOpportunitiesCommand(service));
        commands.Add(feedback);
    }

    private static Command CreateSummaryCommand(FeedbackService service)
    {
        var command = new Command("summary", "Aggregate command outcomes, duration, output, and possible token savings.");
        var options = AddCommonOptions(command, includeLimit: false);
        command.SetAction(parseResult =>
        {
            if (!TryInputs(parseResult.GetValue(options.Format), parseResult.GetValue(options.Since), out var format, out var since))
            {
                return 2;
            }

            var result = service.Summary(parseResult.GetValue(options.Repo) ?? Directory.GetCurrentDirectory(), since);
            RenderSummary(result, format!);
            return result.ExitCode;
        });
        return command;
    }

    private static Command CreateUsageCommand(ToolUsageStore store)
    {
        var command = new Command("usage", "List recent sanitized tool-usage ledger entries.");
        var options = AddCommonOptions(command, includeLimit: true);
        command.SetAction(parseResult =>
        {
            if (!TryInputs(parseResult.GetValue(options.Format), parseResult.GetValue(options.Since), out var format, out var since))
            {
                return 2;
            }

            var result = store.Read(
                parseResult.GetValue(options.Repo) ?? Directory.GetCurrentDirectory(),
                since,
                parseResult.GetValue(options.Limit!));
            RenderUsage(result, format!);
            return result.ExitCode;
        });
        return command;
    }

    private static Command CreateOpportunitiesCommand(FeedbackService service)
    {
        var command = new Command("opportunities", "Find repeated failures and high-output commands that merit feedback-loop improvements.");
        var options = AddCommonOptions(command, includeLimit: false);
        command.SetAction(parseResult =>
        {
            if (!TryInputs(parseResult.GetValue(options.Format), parseResult.GetValue(options.Since), out var format, out var since))
            {
                return 2;
            }

            var result = service.Opportunities(parseResult.GetValue(options.Repo) ?? Directory.GetCurrentDirectory(), since);
            RenderOpportunities(result, format!);
            return result.ExitCode;
        });
        return command;
    }

    private static FeedbackOptions AddCommonOptions(Command command, bool includeLimit)
    {
        var repo = new Option<string>("--repo") { Description = "Initialized repository or workspace path.", DefaultValueFactory = _ => Directory.GetCurrentDirectory() };
        var since = new Option<string?>("--since") { Description = "Optional lookback duration such as 12h, 7d, or 30m." };
        var format = new Option<string>("--format") { Description = "Output format: human, json, or agent.", DefaultValueFactory = _ => "human" };
        Option<int>? limit = null;
        command.Options.Add(repo);
        command.Options.Add(since);
        command.Options.Add(format);
        if (includeLimit)
        {
            limit = new Option<int>("--limit") { Description = "Maximum recent entries, from 1 to 10000.", DefaultValueFactory = _ => 100 };
            command.Options.Add(limit);
        }

        return new FeedbackOptions(repo, since, format, limit);
    }

    private static bool TryInputs(string? selectedFormat, string? selectedSince, out string? format, out DateTimeOffset? since)
    {
        format = (selectedFormat ?? "human").Trim().ToLowerInvariant();
        if (format is not ("human" or "json" or "agent"))
        {
            Console.Error.WriteLine("Unsupported format. Use human, json, or agent.");
            since = null;
            return false;
        }

        if (!FeedbackService.TryParseSince(selectedSince, out since, out var error))
        {
            Console.Error.WriteLine(error);
            return false;
        }

        return true;
    }

    private static void RenderSummary(FeedbackSummary result, string format)
    {
        if (format == "json") { Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions)); return; }
        if (format == "agent")
        {
            Console.WriteLine($"status={result.Status};exitCode={result.ExitCode};invocations={result.InvocationCount};successful={result.SuccessfulCount};failed={result.FailedCount};outputEstimatedTokens={result.OutputEstimatedTokens};baselineEstimatedTokens={result.BaselineEstimatedTokens};actualEstimatedTokens={result.ActualEstimatedTokens};possibleTokenSavings={result.PossibleTokenSavings};possibleTokenSavingsPercent={result.PossibleTokenSavingsPercent};estimatedInvocations={result.EstimatedInvocationCount}");
            foreach (var item in result.Commands) Console.WriteLine($"command={Clean(item.Command)};runs={item.InvocationCount};failed={item.FailedCount};elapsedMs={item.ElapsedMilliseconds};outputEstimatedTokens={item.OutputEstimatedTokens};possibleTokenSavings={item.PossibleTokenSavings}");
            return;
        }

        Console.WriteLine($"Feedback summary: {result.Status}");
        Console.WriteLine($"Invocations: {result.InvocationCount}; successful: {result.SuccessfulCount}; failed: {result.FailedCount}");
        Console.WriteLine($"Estimated tokens: baseline {result.BaselineEstimatedTokens}; actual {result.ActualEstimatedTokens}; possible savings {result.PossibleTokenSavings} ({result.PossibleTokenSavingsPercent}%)");
        Console.WriteLine($"Savings estimates supplied by {result.EstimatedInvocationCount} invocation(s); unestimated commands conservatively report zero savings.");
        foreach (var item in result.Commands) Console.WriteLine($"- {item.Command}: {item.InvocationCount} run(s), {item.FailedCount} failed, ~{item.OutputEstimatedTokens} output tokens, ~{item.PossibleTokenSavings} possible savings");
    }

    private static void RenderUsage(FeedbackUsageResult result, string format)
    {
        if (format == "json") { Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions)); return; }
        if (format == "agent")
        {
            Console.WriteLine($"status={result.Status};exitCode={result.ExitCode};entries={result.Entries.Count}");
            foreach (var item in result.Entries) Console.WriteLine($"usage={item.InvocationId};started={item.StartedAtUtc:O};command={Clean(item.Command)};exitCode={item.ExitCode};elapsedMs={item.ElapsedMilliseconds};outputEstimatedTokens={item.OutputEstimatedTokens};possibleTokenSavings={item.PossibleTokenSavings};basis={Clean(item.SavingsBasis)};confidence={Clean(item.SavingsConfidence)}");
            return;
        }

        Console.WriteLine($"Tool usage: {result.Status}; entries: {result.Entries.Count}");
        foreach (var item in result.Entries) Console.WriteLine($"- {item.StartedAtUtc:u} {item.Command} exit={item.ExitCode} elapsed={item.ElapsedMilliseconds}ms output~{item.OutputEstimatedTokens} tokens savings~{item.PossibleTokenSavings} ({item.SavingsConfidence})");
    }

    private static void RenderOpportunities(FeedbackOpportunityResult result, string format)
    {
        if (format == "json") { Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions)); return; }
        if (format == "agent")
        {
            Console.WriteLine($"status={result.Status};exitCode={result.ExitCode};opportunities={result.Opportunities.Count}");
            foreach (var item in result.Opportunities) Console.WriteLine($"opportunity={item.Code};severity={item.Severity};command={Clean(item.Command)};message={Clean(item.Message)};suggestedAction={Clean(item.SuggestedAction)}");
            return;
        }

        Console.WriteLine($"Feedback opportunities: {result.Status}; count: {result.Opportunities.Count}");
        foreach (var item in result.Opportunities) Console.WriteLine($"- [{item.Severity}] {item.Code} / {item.Command}: {item.Message} {item.SuggestedAction}");
    }

    private static string Clean(string value) => value.Replace('\r', ' ').Replace('\n', ' ').Replace(';', ',').Trim();

    private sealed record FeedbackOptions(Option<string> Repo, Option<string?> Since, Option<string> Format, Option<int>? Limit);
}

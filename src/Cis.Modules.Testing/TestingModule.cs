using System.CommandLine;
using System.Text.Json;
using Cis.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Cis.Modules.Testing;

public sealed class TestingModule : ICisModule
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public string Name => "test";
    public string Description => "Inventory test suites, reconcile native results, and prove executed feature traceability.";

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<ICisTestResultAdapter, JUnitTestResultAdapter>();
        services.AddSingleton<ICisTestResultAdapter, CucumberJUnitTestResultAdapter>();
        services.AddSingleton<ICisTestResultAdapter, VitestJsonResultAdapter>();
        services.AddSingleton<ICisTestResultAdapter, PlaywrightJsonResultAdapter>();
        services.AddSingleton<ICisTestResultAdapter, TrxTestResultAdapter>();
        services.AddSingleton<ICisTestResultAdapter, StrykerJsonResultAdapter>();
        services.AddSingleton<TestingService>();
        services.AddSingleton<IncrementalTestService>();
    }

    public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services)
    {
        var service = services.GetRequiredService<TestingService>();
        var root = new Command(Name, Description);
        root.Subcommands.Add(Simple("inventory", "Discover and persist the registered suite inventory.", service.Inventory));
        root.Subcommands.Add(Validate(service));
        root.Subcommands.Add(Reconcile(service));
        root.Subcommands.Add(Trace(service));
        root.Subcommands.Add(Status(service));
        root.Subcommands.Add(Mutation());
        root.Subcommands.Add(Incremental(services));
        commands.Add(root);
    }

    private static Command Simple(string name, string description, Func<string, TestingResult> action)
    {
        var command = new Command(name, description); var repository = Repo(); var format = Format();
        command.Options.Add(repository); command.Options.Add(format);
        command.SetAction(parse => Render(action(parse.GetValue(repository)!), parse.GetValue(format)!));
        return command;
    }

    private static Command Validate(TestingService service)
    {
        var command = new Command("validate", "Validate the canonical suite profile and result contracts.");
        var repository = Repo(); var format = Format(); var strict = new Option<bool>("--strict");
        command.Options.Add(repository); command.Options.Add(format); command.Options.Add(strict);
        command.SetAction(parse => Render(service.Validate(parse.GetValue(repository)!, parse.GetValue(strict)), parse.GetValue(format)!));
        return command;
    }

    private static Command Reconcile(TestingService service)
    {
        var command = new Command("reconcile", "Parse one workflow run into typed local test evidence.");
        var repository = Repo(); var format = Format(); var run = Required("--run");
        command.Options.Add(repository); command.Options.Add(format); command.Options.Add(run);
        command.SetAction(parse => Render(service.Reconcile(parse.GetValue(repository)!, parse.GetValue(run)!), parse.GetValue(format)!));
        return command;
    }

    private static Command Trace(TestingService service)
    {
        var command = new Command("trace", "Prove generated manual cases executed and passed in a reconciled run.");
        var change = new Argument<string>("change-id"); var repository = Repo(); var format = Format(); var run = Required("--run");
        command.Arguments.Add(change); command.Options.Add(repository); command.Options.Add(format); command.Options.Add(run);
        command.SetAction(parse => Render(service.Trace(parse.GetValue(repository)!, parse.GetValue(change)!, parse.GetValue(run)!), parse.GetValue(format)!));
        return command;
    }

    private static Command Status(TestingService service)
    {
        var command = new Command("status", "Report test traceability for a change using an exact or latest run.");
        var change = new Argument<string>("change-id"); var repository = Repo(); var format = Format(); var run = new Option<string?>("--run");
        command.Arguments.Add(change); command.Options.Add(repository); command.Options.Add(format); command.Options.Add(run);
        command.SetAction(parse => Render(service.Status(parse.GetValue(repository)!, parse.GetValue(change)!, parse.GetValue(run)), parse.GetValue(format)!));
        return command;
    }

    private static Command Mutation()
    {
        var mutation = new Command("mutation", "Run bounded native mutation profiles and assess their unchanged reports.");
        var assess = new Command("assess", "Calculate conservative and native scores for the reported mutants.");
        var repository = Repo(); var format = Format(); var report = Required("--report");
        var threshold = new Option<double>("--break") { Required = true, Description = "Conservative score required, from 0 through 100." };
        assess.Options.Add(repository); assess.Options.Add(format); assess.Options.Add(report); assess.Options.Add(threshold);
        assess.SetAction(parse => AssessMutation(parse.GetValue(repository)!, parse.GetValue(report)!,
            parse.GetValue(threshold), parse.GetValue(format)!));
        mutation.Subcommands.Add(assess);
        var run = new Command("run", "Execute one reusable native mutation profile with time and storage limits.");
        var runRepository = Repo(); var runFormat = Format(); var profile = Required("--profile");
        var kind = new Option<string>("--kind")
        {
            DefaultValueFactory = _ => "diagnostic",
            Description = "canary, diagnostic, or qualification; diagnostic results do not qualify the complete scope."
        };
        run.Options.Add(runRepository); run.Options.Add(runFormat); run.Options.Add(profile); run.Options.Add(kind);
        run.SetAction(parse => RunMutation(new(parse.GetValue(runRepository)!, parse.GetValue(profile)!,
            parse.GetValue(kind)!), parse.GetValue(runFormat)!));
        mutation.Subcommands.Add(run);
        return mutation;
    }

    private static Command Incremental(IServiceProvider services)
    {
        var root = new Command("incremental", "Plan and run graph-guided unit and mutation batches with reusable results.");
        foreach (var operation in new[] { "plan", "run", "status" })
        {
            var command = new Command(operation, operation == "run" ? "Execute bounded pending batches and save each completed result." : "Inspect current and pending batches without executing tests.");
            var repository = Repo(); var format = Format(); var profile = Required("--profile");
            var layer = new Option<string>("--layer") { DefaultValueFactory = _ => "all", Description = "unit, mutation, or all." };
            var batches = new Option<int>("--max-batches") { DefaultValueFactory = _ => 4 };
            var seconds = new Option<int>("--max-seconds") { DefaultValueFactory = _ => 1200 };
            var force = new Option<bool>("--force") { Description = "Rerun current results as well as pending work." };
            command.Options.Add(repository); command.Options.Add(format); command.Options.Add(profile);
            command.Options.Add(layer); command.Options.Add(batches); command.Options.Add(seconds); command.Options.Add(force);
            command.SetAction(parse =>
            {
                var output = parse.GetValue(format)!;
                if (output is not ("human" or "agent" or "json")) return 2;
                var reader = services.GetService<ICisGraphSnapshotReader>();
                if (reader is null) return RenderIncremental(new("missing-graph-module", 2) { Diagnostics = ["The installed graph module is required."] }, output);
                var service = new IncrementalTestService(reader);
                var options = new IncrementalTestOptions(parse.GetValue(repository)!, parse.GetValue(profile)!, parse.GetValue(layer)!,
                    parse.GetValue(batches), parse.GetValue(seconds), parse.GetValue(force));
                return RenderIncremental(operation == "run" ? service.Run(options) : service.Plan(options), output);
            });
            root.Subcommands.Add(command);
        }
        return root;
    }

    private static int RenderIncremental(IncrementalTestResult result, string format)
    {
        if (format == "json") Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        else if (format == "agent")
        {
            Console.WriteLine($"status={result.Status};exitCode={result.ExitCode};profile={result.ProfileId};layer={result.Layer};scopeComplete={result.ScopeComplete.ToString().ToLowerInvariant()};qualified=false");
            Console.WriteLine($"executedUnitBatches={result.ExecutedUnitBatches};executedMutationBatches={result.ExecutedMutationBatches};reusedUnitBatches={result.ReusedUnitBatches};reusedMutationBatches={result.ReusedMutationBatches};pendingUnitBatches={result.PendingUnitBatches};pendingMutationBatches={result.PendingMutationBatches}");
            Console.WriteLine(FormattableString.Invariant($"knownScore={result.KnownScore};completeScopeScore={result.CompleteScopeScore};killed={result.Killed};knownDenominator={result.KnownDenominator};durationMilliseconds={result.DurationMilliseconds};retainedBytes={result.RetainedBytes}"));
            foreach (var batch in result.Batches) Console.WriteLine($"source={batch.SourceFile};selection={batch.Selection};unitCurrent={batch.UnitCurrent.ToString().ToLowerInvariant()};mutationCurrent={batch.MutationCurrent.ToString().ToLowerInvariant()}");
            foreach (var diagnostic in result.Diagnostics) Console.WriteLine($"diagnostic={diagnostic.Replace(';', ',')}");
        }
        else
        {
            Console.WriteLine($"Incremental {result.Layer}: {result.Status}; {result.PendingUnitBatches} unit batches and {result.PendingMutationBatches} mutation batches pending.");
            Console.WriteLine($"Executed: {result.ExecutedUnitBatches} unit, {result.ExecutedMutationBatches} mutation. Reused: {result.ReusedUnitBatches} unit, {result.ReusedMutationBatches} mutation.");
            if (result.CompleteScopeScore is { } score) Console.WriteLine(FormattableString.Invariant($"Current declared-scope mutation score: {score:F6}% ({result.Killed}/{result.KnownDenominator})."));
            else if (result.KnownScore is { } known) Console.WriteLine(FormattableString.Invariant($"Completed batches only: {known:F6}% ({result.Killed}/{result.KnownDenominator}); full scope remains incomplete."));
            foreach (var diagnostic in result.Diagnostics) Console.WriteLine(diagnostic);
            Console.WriteLine("Development feedback only; release qualification remains separate.");
        }
        return result.ExitCode;
    }

    private static int RunMutation(MutationRunOptions options, string format)
    {
        if (format is not ("json" or "agent" or "human"))
        {
            Console.Error.WriteLine("Unsupported format. Use human, json, or agent.");
            return 2;
        }
        var result = new MutationRunService().Run(options);
        if (format == "json") Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        else if (format == "agent")
        {
            Console.WriteLine($"status={result.Status};exitCode={result.ExitCode};profile={result.ProfileId ?? "none"};runId={result.RunId ?? "none"};kind={result.Kind};partial={result.Partial.ToString().ToLowerInvariant()};qualified={result.Qualified.ToString().ToLowerInvariant()}");
            if (result.Assessment?.Mutation is { } mutation)
                Console.WriteLine(FormattableString.Invariant($"conservativeScore={mutation.Score};nativeScore={mutation.NativeScore};killed={mutation.Killed};denominator={mutation.Denominator};report={result.ReportPath}"));
            foreach (var diagnostic in result.Diagnostics) Console.WriteLine($"diagnostic={diagnostic.Replace(';', ',')}");
        }
        else
        {
            Console.WriteLine($"Mutation {result.Kind}: {result.Status} ({result.ProfileId ?? "invalid profile"}).");
            if (result.Assessment?.Mutation is { } mutation)
                Console.WriteLine(FormattableString.Invariant($"Conservative score: {mutation.Score:F6}% ({mutation.Killed}/{mutation.Denominator}); native score: {mutation.NativeScore:F6}%."));
            if (result.SummaryPath is { } summary) Console.WriteLine($"Run summary: {summary}");
            foreach (var diagnostic in result.Diagnostics) Console.WriteLine(diagnostic);
            Console.WriteLine("Qualification applies to the declared profile scope; project release requirements remain separate.");
        }
        return result.ExitCode;
    }

    private static int AssessMutation(string repository, string report, double threshold, string format)
    {
        if (format is not ("json" or "agent" or "human"))
        {
            Console.Error.WriteLine("Unsupported format. Use human, json, or agent.");
            return 2;
        }
        var suite = new TestSuiteProfile("mutation-assessment", "report", "mutation", "stryker", "assess", ".",
            "stryker-json", report, "-", "-", "-", "always", "local", "read-only");
        TestSuiteExecution result;
        try
        {
            repository = Path.GetFullPath(repository);
            var path = Path.GetFullPath(report, repository);
            result = new StrykerJsonResultAdapter().Read(new(repository, suite, path, null, null, threshold));
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException
            or ArgumentException or NotSupportedException)
        {
            result = new(suite.Id, suite.Layer, suite.Framework, "invalid-evidence", TestFailureKind.InvalidEvidence,
                0, 0, 0, 0, 0, [], null, null, [], [$"Mutation report could not be assessed: {error.Message}"]);
        }
        if (format == "json") Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        else if (format == "agent")
        {
            Console.WriteLine(FormattableString.Invariant($"status={result.Status};qualified={result.Mutation?.Qualified.ToString().ToLowerInvariant() ?? "false"};conservativeScore={result.Mutation?.Score};nativeScore={result.Mutation?.NativeScore};denominator={result.Mutation?.Denominator};reportSha256={result.Mutation?.ReportSha256 ?? "none"}"));
            foreach (var diagnostic in result.Diagnostics) Console.WriteLine($"diagnostic={diagnostic.Replace(';', ',')}");
            Console.WriteLine("scope=reported-mutants;sourceFreshnessCertified=false;completeScopeCertified=false;releaseReadinessCertified=false");
        }
        else
        {
            Console.WriteLine($"Mutation assessment: {result.Status}.");
            if (result.Mutation is { } summary)
                Console.WriteLine(FormattableString.Invariant($"Conservative score: {summary.Score:F6}% ({summary.Killed}/{summary.Denominator}); native score: {summary.NativeScore:F6}%."));
            foreach (var diagnostic in result.Diagnostics) Console.WriteLine(diagnostic);
            Console.WriteLine("This assesses the report only; it does not certify source freshness, full scope, or release readiness.");
        }
        return result.Mutation?.Qualified == true ? 0 : 4;
    }

    private static int Render(TestingResult result, string format)
    {
        if (format == "json") Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        else if (format == "agent")
        {
            Console.WriteLine($"status={result.Status};exitCode={result.ExitCode};runId={result.RunId ?? "none"};suites={result.Suites.Count};trace={result.Trace.Count};applied={result.Applied.ToString().ToLowerInvariant()}");
            foreach (var suite in result.Manifest?.Suites ?? []) Console.WriteLine($"suite={suite.SuiteId};layer={suite.Layer};status={suite.Status};total={suite.Total};passed={suite.Passed};failed={suite.Failed};skipped={suite.Skipped};failureKind={suite.FailureKind}");
            foreach (var trace in result.Trace) Console.WriteLine($"case={trace.Id};status={trace.Status};suites={string.Join(',', trace.Suites)}");
            foreach (var diagnostic in result.Diagnostics) Console.WriteLine($"diagnostic={diagnostic.Replace(';', ',')}");
        }
        else if (format == "human")
        {
            Console.WriteLine($"Testing: {result.Status}; run={result.RunId ?? "none"}; suites={result.Suites.Count}");
            foreach (var suite in result.Manifest?.Suites ?? []) Console.WriteLine($"- {suite.SuiteId} [{suite.Layer}]: {suite.Status} ({suite.Passed}/{suite.Total} passed)");
            foreach (var trace in result.Trace) Console.WriteLine($"- {trace.Id}: {trace.Status}");
            foreach (var diagnostic in result.Diagnostics) Console.WriteLine(diagnostic);
        }
        else return 2;
        return result.ExitCode;
    }

    private static Option<string> Required(string name) => new(name) { Required = true };
    private static Option<string> Repo() => new("--repo") { DefaultValueFactory = _ => Directory.GetCurrentDirectory() };
    private static Option<string> Format() => new("--format") { DefaultValueFactory = _ => "human" };
}

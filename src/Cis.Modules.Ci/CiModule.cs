using System.CommandLine;
using System.Text.Json;
using Cis.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Cis.Modules.Ci;

public sealed class CiModule : ICisModule
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public string Name => "ci";
    public string Description => "Investigate remote CI checks, runs, jobs, logs, artifacts, and failures through explicit providers.";
    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<CiService>();
        services.AddSingleton<ICisRepositoryDoctorCheck, CiDoctorCheck>();
    }
    public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services)
    {
        var service = services.GetRequiredService<CiService>(); var root = new Command(Name, Description);
        root.Subcommands.Add(Providers(service)); root.Subcommands.Add(Status(service)); root.Subcommands.Add(Runs(service));
        root.Subcommands.Add(RunValue("jobs", "List jobs for one workflow run.", "--run", service.Jobs));
        root.Subcommands.Add(RunValue("artifacts", "List artifact metadata for one workflow run.", "--run", service.Artifacts));
        root.Subcommands.Add(RunValue("diagnose", "Classify failed jobs and preserve bounded redacted logs.", "--run", service.Diagnose));
        root.Subcommands.Add(RunValue("reproduce", "Suggest focused local reproduction commands from failed-job evidence.", "--run", service.Reproduce));
        root.Subcommands.Add(Logs(service)); root.Subcommands.Add(Rerun(service)); commands.Add(root);
    }

    private static Command Providers(CiService service)
    {
        var command = new Command("providers", "Probe loaded CI providers and the resolved remote target."); var common = Common(command);
        command.SetAction(parse => Render(service.Providers(parse.GetValue(common.Repo)!, parse.GetValue(common.Repository)), parse.GetValue(common.Format)!)); return command;
    }
    private static Command Status(CiService service)
    {
        var command = new Command("status", "Inspect checks and workflow runs for one pull request."); var pr = new Option<int>("--pr") { Required = true };
        var common = Common(command); command.Options.Add(pr);
        command.SetAction(parse => Render(service.Status(parse.GetValue(common.Repo)!, parse.GetValue(common.Provider), parse.GetValue(common.Repository), parse.GetValue(pr)), parse.GetValue(common.Format)!)); return command;
    }
    private static Command Runs(CiService service)
    {
        var command = new Command("runs", "List bounded workflow runs, optionally for one pull request.");
        var pr = new Option<int?>("--pr"); var limit = new Option<int>("--limit") { DefaultValueFactory = _ => 20 };
        var common = Common(command); command.Options.Add(pr); command.Options.Add(limit);
        command.SetAction(parse => Render(service.Runs(parse.GetValue(common.Repo)!, parse.GetValue(common.Provider), parse.GetValue(common.Repository), parse.GetValue(pr), parse.GetValue(limit)), parse.GetValue(common.Format)!)); return command;
    }
    private static Command RunValue(string name, string description, string optionName, Func<string, string?, string?, long, CiInvestigationResult> action)
    {
        var command = new Command(name, description); var id = new Option<long>(optionName) { Required = true }; var common = Common(command); command.Options.Add(id);
        command.SetAction(parse => Render(action(parse.GetValue(common.Repo)!, parse.GetValue(common.Provider), parse.GetValue(common.Repository), parse.GetValue(id)), parse.GetValue(common.Format)!)); return command;
    }
    private static Command Logs(CiService service)
    {
        var command = new Command("logs", "Download one job log into bounded redacted local evidence."); var job = new Option<long>("--job") { Required = true }; var common = Common(command); command.Options.Add(job);
        command.SetAction(parse => Render(service.Logs(parse.GetValue(common.Repo)!, parse.GetValue(common.Provider), parse.GetValue(common.Repository), parse.GetValue(job)), parse.GetValue(common.Format)!)); return command;
    }
    private static Command Rerun(CiService service)
    {
        var command = new Command("rerun-failed", "Request a remote rerun of failed jobs after explicit confirmation.");
        var run = new Option<long>("--run") { Required = true }; var yes = new Option<bool>("--yes") { Description = "Confirm the remote mutation." }; var common = Common(command); command.Options.Add(run); command.Options.Add(yes);
        command.SetAction(parse => Render(service.RerunFailed(parse.GetValue(common.Repo)!, parse.GetValue(common.Provider), parse.GetValue(common.Repository), parse.GetValue(run), parse.GetValue(yes)), parse.GetValue(common.Format)!)); return command;
    }
    private static CommonOptions Common(Command command)
    {
        var result = new CommonOptions(new("--repo") { DefaultValueFactory = _ => Directory.GetCurrentDirectory() },
            new("--provider") { Description = "Loaded CI provider kind; inferred when exactly one exists." },
            new("--repository") { Description = "Remote owner/repository; inferred from GITHUB_REPOSITORY or origin." },
            new("--format") { DefaultValueFactory = _ => "human" });
        command.Options.Add(result.Repo); command.Options.Add(result.Provider); command.Options.Add(result.Repository); command.Options.Add(result.Format); return result;
    }
    private static int Render(CiInvestigationResult result, string format)
    {
        if (format == "json") Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        else if (format == "agent")
        {
            Console.WriteLine($"status={result.Status};exitCode={result.ExitCode};provider={Safe(result.Provider)};target={Safe(result.Target)};checks={result.Checks.Count};runs={result.Runs.Count};jobs={result.Jobs.Count};artifacts={result.Artifacts.Count};failures={result.Failures.Count};evidence={result.Evidence.Count};applied={result.Applied.ToString().ToLowerInvariant()}");
            foreach (var availability in result.Availability) Console.WriteLine($"availability={availability.Available.ToString().ToLowerInvariant()};message={Safe(availability.Message)}");
            foreach (var check in result.Checks) Console.WriteLine($"check={check.Id};name={Safe(check.Name)};status={check.Status};conclusion={Safe(check.Conclusion)};url={Safe(check.Url)}");
            foreach (var run in result.Runs) Console.WriteLine($"run={run.Id};name={Safe(run.Name)};status={run.Status};conclusion={Safe(run.Conclusion)};attempt={run.Attempt};sha={Safe(run.HeadSha)};url={Safe(run.Url)}");
            foreach (var job in result.Jobs) Console.WriteLine($"job={job.Id};run={job.RunId};name={Safe(job.Name)};status={job.Status};conclusion={Safe(job.Conclusion)};runner={Safe(job.RunnerName)}");
            foreach (var artifact in result.Artifacts) Console.WriteLine($"artifact={artifact.Id};name={Safe(artifact.Name)};bytes={artifact.SizeBytes};expired={artifact.Expired.ToString().ToLowerInvariant()}");
            foreach (var failure in result.Failures) Console.WriteLine($"failure={failure.JobId};classification={failure.Classification};job={Safe(failure.Job)};step={Safe(failure.Step)};reproduce={Safe(string.Join(',', failure.ReproductionCommands))}");
            foreach (var evidence in result.Evidence) Console.WriteLine($"evidence={evidence.Kind};path={Safe(evidence.Path)};sha256={evidence.Sha256};bytes={evidence.Bytes};truncated={evidence.Truncated.ToString().ToLowerInvariant()};redaction={Safe(evidence.Redaction)}");
            foreach (var error in result.Errors) Console.WriteLine($"error={Safe(error)}");
        }
        else if (format == "human")
        {
            Console.WriteLine($"CI investigation: {result.Status}; provider: {result.Provider}; target: {result.Target ?? "unavailable"}");
            Console.WriteLine($"Checks: {result.Checks.Count}; runs: {result.Runs.Count}; jobs: {result.Jobs.Count}; artifacts: {result.Artifacts.Count}; failures: {result.Failures.Count}");
            foreach (var failure in result.Failures) Console.WriteLine($"- [{failure.Classification}] {failure.Job} / {failure.Step}\n  Reproduce: {string.Join("; ", failure.ReproductionCommands)}");
            foreach (var error in result.Errors) Console.WriteLine($"ERROR: {error}");
        }
        else { Console.Error.WriteLine("Unsupported format. Use human, json, or agent."); return 2; }
        return result.ExitCode;
    }
    private static string Safe(string? value) => string.IsNullOrWhiteSpace(value) ? "none" : value.Replace(';', ',').Replace('\r', ' ').Replace('\n', ' ');
    private sealed record CommonOptions(Option<string> Repo, Option<string?> Provider, Option<string?> Repository, Option<string> Format);
}

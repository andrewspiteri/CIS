using System.CommandLine;
using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Repository;
using Microsoft.Extensions.DependencyInjection;

namespace Cis.Modules.References;

public sealed class ReferencesModule : ICisModule
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public string Name => "references";
    public string Description => "Discover, correlate, validate, and compare governed engineering references.";

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<ICisReferenceProvider, ConfigurationReferenceProvider>();
        services.AddSingleton<ICisReferenceProvider, PermissionReferenceProvider>();
        services.AddSingleton<ICisReferenceProvider, CommandReferenceProvider>();
        services.AddSingleton<ICisReferenceProvider, EventReferenceProvider>();
        services.AddSingleton<ICisReferenceProvider, WorkflowStateReferenceProvider>();
        services.AddSingleton<ICisReferenceProvider>(_ => new StableLiteralReferenceProvider("business-invariant-catalogue", "Stable business invariant identities.", "INV"));
        services.AddSingleton<ICisReferenceProvider, ProjectionReferenceProvider>();
        services.AddSingleton<ICisReferenceProvider>(_ => new StableLiteralReferenceProvider("problem-details-catalogue", "Stable Problem Details identities.", "PROB"));
        services.AddSingleton<ICisReferenceProvider, PackageReferenceProvider>();
        services.AddSingleton<ICisReferenceProvider, ScreenRouteReferenceProvider>();
        services.AddSingleton<ICisReferenceProvider, ModuleOwnershipReferenceProvider>();
        services.AddSingleton<ICisReferenceProvider, DataReferenceProvider>();
        services.AddSingleton<ICisReferenceProvider, ErdReferenceProvider>();
        services.AddSingleton<ReferenceGovernanceService>();
        services.AddSingleton<DocumentationCatalogMerger>();
        services.AddSingleton<SourceEvidenceProjectionService>();
        services.AddSingleton<ICisSourceEvidenceRegistrar>(provider => provider.GetRequiredService<SourceEvidenceProjectionService>());
        services.AddSingleton<ICisBrdSourceEvidenceProvider>(provider => provider.GetRequiredService<SourceEvidenceProjectionService>());
        services.AddSingleton<ICisRepositoryDoctorCheck, ReferenceDoctorCheck>();
    }

    public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services)
    {
        var service = services.GetRequiredService<ReferenceGovernanceService>();
        var root = new Command(Name, Description);
        root.Subcommands.Add(Discover(service));
        root.Subcommands.Add(Inventory(service));
        root.Subcommands.Add(Validate(service));
        root.Subcommands.Add(Reconcile(service));
        root.Subcommands.Add(Diff(service));
        root.Subcommands.Add(SourceEvidence(services.GetRequiredService<SourceEvidenceProjectionService>()));
        commands.Add(root);
    }

    private static Command SourceEvidence(SourceEvidenceProjectionService service)
    {
        var source = new Command("source", "Register source documents and build deterministic Markdown routing projections.");

        var importCommand = new Command("import", "Register one source document or initialized repository and build its local projection.");
        var file = new Option<string>("--file") { Required = true, Description = "A repository-owned .docx, .md, .txt, or initialized repository path." };
        var assessment = new Option<string>("--assessment") { DefaultValueFactory = _ => "Reference", Description = "Unreviewed, Reference, Adopted, or Rejected." };
        var actor = new Option<string>("--actor") { Required = true }; var reason = new Option<string>("--reason") { Required = true };
        var importRepo = Repo(); var importFormat = Format();
        foreach (var option in new Option[] { file, assessment, actor, reason, importRepo, importFormat }) importCommand.Options.Add(option);
        importCommand.SetAction(parse => Render(service.Import(parse.GetValue(importRepo)!, parse.GetValue(file)!,
            parse.GetValue(assessment)!, parse.GetValue(actor)!, parse.GetValue(reason)!), parse.GetValue(importFormat)!, "source-import"));
        source.Subcommands.Add(importCommand);

        var build = new Command("build", "Rebuild derived content, source map, index card, and change diff without changing the accepted digest.");
        var buildId = new Option<string?>("--id"); var buildRepo = Repo(); var buildFormat = Format();
        build.Options.Add(buildId); build.Options.Add(buildRepo); build.Options.Add(buildFormat);
        build.SetAction(parse => Render(service.Build(parse.GetValue(buildRepo)!, parse.GetValue(buildId)), parse.GetValue(buildFormat)!, "source-build"));
        source.Subcommands.Add(build);

        var status = new Command("status", "Compare registered and detected source digests without modifying files.");
        var statusId = new Option<string?>("--id"); var statusRepo = Repo(); var statusFormat = Format();
        status.Options.Add(statusId); status.Options.Add(statusRepo); status.Options.Add(statusFormat);
        status.SetAction(parse => Render(service.Status(parse.GetValue(statusRepo)!, parse.GetValue(statusId)), parse.GetValue(statusFormat)!, "source-status"));
        source.Subcommands.Add(status);

        var accept = new Command("accept", "Accept a reviewed source revision as the new registered digest without rewriting the BRD.");
        var acceptId = new Argument<string>("source-id"); var acceptActor = new Option<string>("--actor") { Required = true };
        var acceptReason = new Option<string>("--reason") { Required = true }; var yes = new Option<bool>("--yes") { Description = "Confirm the digest transition." };
        var acceptRepo = Repo(); var acceptFormat = Format(); accept.Arguments.Add(acceptId);
        foreach (var option in new Option[] { acceptActor, acceptReason, yes, acceptRepo, acceptFormat }) accept.Options.Add(option);
        accept.SetAction(parse => Render(service.Accept(parse.GetValue(acceptRepo)!, parse.GetValue(acceptId)!,
            parse.GetValue(acceptActor)!, parse.GetValue(acceptReason)!, parse.GetValue(yes)), parse.GetValue(acceptFormat)!, "source-accept"));
        source.Subcommands.Add(accept);
        return source;
    }

    private static Command Discover(ReferenceGovernanceService service)
    {
        var command = new Command("discover", "Extract source identities and correlate them with canonical reference tables.");
        var repo = Repo(); var format = Format(); command.Options.Add(repo); command.Options.Add(format);
        command.SetAction(parse => Render(service.Discover(parse.GetValue(repo)!), parse.GetValue(format)!, "discover"));
        return command;
    }

    private static Command Inventory(ReferenceGovernanceService service)
    {
        var command = new Command("inventory", "Read normalized reference state without rescanning source.");
        var repo = Repo(); var format = Format(); var kind = new Option<string?>("--kind") { Description = "Optional exact reference kind." };
        command.Options.Add(repo); command.Options.Add(kind); command.Options.Add(format);
        command.SetAction(parse => Render(service.Inventory(parse.GetValue(repo)!, parse.GetValue(kind)), parse.GetValue(format)!, "inventory"));
        return command;
    }

    private static Command Validate(ReferenceGovernanceService service)
    {
        var command = new Command("validate", "Validate provider conflicts, canonical identities, source drift, and evidence paths.");
        var repo = Repo(); var format = Format(); var strict = new Option<bool>("--strict") { Description = "Treat warnings as validation failures." };
        var noRefresh = new Option<bool>("--no-refresh") { Description = "Validate existing normalized state without source discovery." };
        command.Options.Add(repo); command.Options.Add(strict); command.Options.Add(noRefresh); command.Options.Add(format);
        command.SetAction(parse => Render(service.Validate(parse.GetValue(repo)!, parse.GetValue(strict), !parse.GetValue(noRefresh)), parse.GetValue(format)!, "validate"));
        return command;
    }

    private static Command Diff(ReferenceGovernanceService service)
    {
        var command = new Command("diff", "Compare governed references and source identities with a Git baseline.");
        var baseline = new Option<string>("--base") { Description = "Required Git commit, branch, or tag baseline.", Required = true };
        var repo = Repo(); var format = Format(); command.Options.Add(baseline); command.Options.Add(repo); command.Options.Add(format);
        command.SetAction(parse => Render(service.Diff(parse.GetValue(repo)!, parse.GetValue(baseline)!), parse.GetValue(format)!, "diff"));
        return command;
    }

    private static Command Reconcile(ReferenceGovernanceService service)
    {
        var command = new Command("reconcile", "Add reviewed source identities to supported canonical reference tables without replacing existing rows.");
        var kind = new Option<string[]>("--kind") { Description = "Optional supported kind; repeat as needed.", Arity = ArgumentArity.ZeroOrMore, AllowMultipleArgumentsPerToken = true };
        var yes = new Option<bool>("--yes") { Description = "Confirm the exact additive reconciliation." };
        var repo = Repo(); var format = Format(); command.Options.Add(kind); command.Options.Add(yes); command.Options.Add(repo); command.Options.Add(format);
        command.SetAction(parse => Render(service.Reconcile(parse.GetValue(repo)!, parse.GetValue(kind) ?? [], parse.GetValue(yes)), parse.GetValue(format)!, "reconcile"));
        return command;
    }

    private static int Render(object result, string format, string operation)
    {
        var exitCode = result switch
        {
            ReferenceDiscoveryResult item => item.ExitCode,
            ReferenceInventoryResult item => item.ExitCode,
            ReferenceValidationResult item => item.ExitCode,
            ReferenceDiffResult item => item.ExitCode,
            ReferenceReconcileResult item => item.ExitCode,
            SourceEvidenceResult item => item.ExitCode,
            _ => 2,
        };
        if (format == "json") Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        else if (format == "agent") RenderAgent(result, operation, exitCode);
        else if (format == "human") RenderHuman(result, operation);
        else { Console.Error.WriteLine("Unsupported format. Use human, json, or agent."); return 2; }
        return exitCode;
    }

    private static void RenderAgent(object result, string operation, int exitCode)
    {
        switch (result)
        {
            case ReferenceDiscoveryResult item:
                Console.WriteLine($"status={item.Status};operation={operation};exitCode={exitCode};families={item.Families.Count};canonical={item.Families.Sum(family => family.CanonicalEntries.Count)};observations={item.Families.Sum(family => family.Observations.Count)};applied={item.Applied.ToString().ToLowerInvariant()};digest={Safe(item.Digest)}");
                Diagnostics(item.Diagnostics); break;
            case ReferenceInventoryResult item:
                Console.WriteLine($"status={item.Status};operation={operation};exitCode={exitCode};families={item.Families.Count};canonical={item.Families.Sum(family => family.CanonicalEntries.Count)};observations={item.Families.Sum(family => family.Observations.Count)};digest={Safe(item.Digest)}");
                foreach (var error in item.Errors) Console.WriteLine($"error={Safe(error)}"); break;
            case ReferenceValidationResult item:
                Console.WriteLine($"status={item.Status};operation={operation};exitCode={exitCode};errors={item.Errors};warnings={item.Warnings};strict={item.Strict.ToString().ToLowerInvariant()}");
                Diagnostics(item.Diagnostics); break;
            case ReferenceDiffResult item:
                Console.WriteLine($"status={item.Status};operation={operation};exitCode={exitCode};baseline={Safe(item.Baseline)};current={Safe(item.CurrentRevision)};changes={item.Changes.Count}");
                foreach (var change in item.Changes) Console.WriteLine($"change={change.Change};kind={change.Kind};identity={Safe(change.Identity)};message={Safe(change.Message)}");
                Diagnostics(item.Diagnostics); break;
            case ReferenceReconcileResult item:
                Console.WriteLine($"status={item.Status};operation={operation};exitCode={exitCode};added={item.Added};updated={item.UpdatedPaths.Count};applied={item.Applied.ToString().ToLowerInvariant()};confirmationRequired={item.ConfirmationRequired.ToString().ToLowerInvariant()}");
                foreach (var path in item.UpdatedPaths) Console.WriteLine($"updated={Safe(path)}"); foreach (var row in item.ProposedRows) Console.WriteLine($"proposed={Safe(row)}"); foreach (var diagnostic in item.Diagnostics) Console.WriteLine($"diagnostic={Safe(diagnostic)}"); break;
            case SourceEvidenceResult item:
                Console.WriteLine($"status={item.Status};operation={operation};exitCode={exitCode};sources={item.Sources.Count};applied={item.Applied.ToString().ToLowerInvariant()};confirmationRequired={item.ConfirmationRequired.ToString().ToLowerInvariant()}");
                Console.WriteLine($"registry={Safe(item.RegistryPath)}");
                foreach (var source in item.Sources) Console.WriteLine($"source={Safe(source.Id)};path={Safe(source.SourcePath)};status={source.Status};registered={Safe(source.RegisteredDigest)};detected={Safe(source.DetectedDigest)};materialToBrd={source.MaterialToBrd.ToString().ToLowerInvariant()};projection={Safe(source.ProjectionPath)}");
                foreach (var diagnostic in item.Diagnostics) Console.WriteLine($"diagnostic={Safe(diagnostic)}"); break;
        }
    }

    private static void RenderHuman(object result, string operation)
    {
        switch (result)
        {
            case ReferenceDiscoveryResult item:
                Console.WriteLine($"Reference {operation}: {item.Status}; families: {item.Families.Count}; canonical entries: {item.Families.Sum(family => family.CanonicalEntries.Count)}; source observations: {item.Families.Sum(family => family.Observations.Count)}");
                HumanDiagnostics(item.Diagnostics); break;
            case ReferenceInventoryResult item:
                Console.WriteLine($"Reference {operation}: {item.Status}; families: {item.Families.Count}");
                foreach (var family in item.Families) Console.WriteLine($"- {family.Kind}: {family.CanonicalEntries.Count} canonical, {family.Observations.Count} discovered");
                foreach (var error in item.Errors) Console.WriteLine($"- error: {error}"); break;
            case ReferenceValidationResult item:
                Console.WriteLine($"Reference {operation}: {item.Status}; errors: {item.Errors}; warnings: {item.Warnings}"); HumanDiagnostics(item.Diagnostics); break;
            case ReferenceDiffResult item:
                Console.WriteLine($"Reference {operation}: {item.Status}; changes: {item.Changes.Count}; baseline: {item.Baseline}");
                foreach (var change in item.Changes) Console.WriteLine($"- [{change.Change}] {change.Kind}: {change.Identity}"); HumanDiagnostics(item.Diagnostics); break;
            case ReferenceReconcileResult item:
                Console.WriteLine($"Reference {operation}: {item.Status}; added: {item.Added}; updated files: {item.UpdatedPaths.Count}");
                foreach (var path in item.UpdatedPaths) Console.WriteLine($"- {path}"); foreach (var row in item.ProposedRows) Console.WriteLine($"  + {row}"); foreach (var diagnostic in item.Diagnostics) Console.WriteLine($"- {diagnostic}"); break;
            case SourceEvidenceResult item:
                Console.WriteLine($"Reference {operation}: {item.Status}; sources: {item.Sources.Count}");
                foreach (var source in item.Sources) Console.WriteLine($"- {source.Id}: {source.Status}; {source.SourcePath}; material to BRD: {source.MaterialToBrd}");
                foreach (var diagnostic in item.Diagnostics) Console.WriteLine($"- {diagnostic}"); break;
        }
    }

    private static void Diagnostics(IEnumerable<ReferenceDiagnostic> items)
    { foreach (var item in items) Console.WriteLine($"diagnostic={item.Code};severity={item.Severity};kind={item.Kind};message={Safe(item.Message)};remediation={Safe(item.Remediation)};evidence={Safe(string.Join(',', item.Evidence))}"); }
    private static void HumanDiagnostics(IEnumerable<ReferenceDiagnostic> items)
    { foreach (var item in items) Console.WriteLine($"- [{item.Severity}] {item.Code} ({item.Kind}) {item.Message}\n  Fix: {item.Remediation}"); }
    private static Option<string> Repo() => new("--repo") { Description = "Initialized repository path. Defaults to current directory.", DefaultValueFactory = _ => Directory.GetCurrentDirectory() };
    private static Option<string> Format() => new("--format") { Description = "Output format: human, json, or agent.", DefaultValueFactory = _ => "human" };
    private static string Safe(string? value) => string.IsNullOrWhiteSpace(value) ? "none" : value.Replace(';', ',').Replace('\r', ' ').Replace('\n', ' ');
}

using System.CommandLine;
using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Docs;
using Cis.Modules.Repository;
using Microsoft.Extensions.DependencyInjection;

namespace Cis.Modules.Standards;

public sealed class StandardsModule : ICisModule
{
    private static readonly string[] Formats = ["human", "json", "agent"];
    public string Name => "standards";
    public string Description => "Inventory, route, validate, infer, and inspect conformance of repository standards.";

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<DocumentationCatalogReader>();
        services.AddSingleton<DocumentationCatalogMerger>();
        services.AddSingleton<StandardsGovernanceService>();
        services.AddSingleton<StandardImportService>();
        services.AddSingleton<ICisStandardPatternProvider, BuiltInStandardPatternProvider>();
        services.AddSingleton(provider => new StandardPatternCatalogService(
            provider.GetRequiredService<ICisRepositoryContextResolver>(),
            provider.GetServices<ICisStandardPatternProvider>(),
            provider.GetService<ICisGraphSnapshotReader>()));
        services.AddSingleton(provider => new StandardInferenceService(
            provider.GetRequiredService<StandardPatternCatalogService>(),
            provider.GetService<ICisGraphSnapshotReader>()));
        services.AddSingleton(provider => new StandardAuditService(
            provider.GetRequiredService<ICisRepositoryContextResolver>(),
            provider.GetRequiredService<StandardsGovernanceService>(),
            provider.GetService<ICisTextGenerationService>()));
        services.AddSingleton<ICisRepositoryDoctorCheck, StandardsDoctorCheck>();
    }

    public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services)
    {
        var service = services.GetRequiredService<StandardsGovernanceService>();
        var root = new Command(Name, Description);
        root.Subcommands.Add(Inventory(service));
        root.Subcommands.Add(Applicable(service));
        root.Subcommands.Add(Validate(service));
        root.Subcommands.Add(Conformance(service));
        root.Subcommands.Add(Import(services.GetRequiredService<StandardImportService>()));
        root.Subcommands.Add(Audit(services.GetRequiredService<StandardAuditService>()));
        root.Subcommands.Add(Patterns(services.GetRequiredService<StandardPatternCatalogService>()));
        root.Subcommands.Add(Infer(services.GetRequiredService<StandardInferenceService>()));
        commands.Add(root);
    }

    private static Command Inventory(StandardsGovernanceService service)
    {
        var command = new Command("inventory", "List cataloged standards, optionally filtered by target, stack, or lifecycle status.");
        var repo = Repo(); var target = Many("--target", "Governed target filter; repeat for more than one.");
        var stack = Many("--stack", "Technology stack filter; repeat for more than one.");
        var status = new Option<string?>("--status") { Description = "Lifecycle status filter." }; var format = Format();
        command.Options.Add(repo); command.Options.Add(target); command.Options.Add(stack); command.Options.Add(status); command.Options.Add(format);
        command.SetAction(parse => Render(service.Inventory(parse.GetValue(repo)!, parse.GetValue(target), parse.GetValue(stack), parse.GetValue(status)), GetFormat(parse.GetValue(format))));
        return command;
    }

    private static Command Applicable(StandardsGovernanceService service)
    {
        var command = new Command("applicable", "Resolve active standards applicable to one or more governed targets and stacks.");
        var repo = Repo(); var target = Many("--target", "Required governed target; repeat for more than one.");
        var stack = Many("--stack", "Technology stack; repeat for more than one."); var format = Format();
        command.Options.Add(repo); command.Options.Add(target); command.Options.Add(stack); command.Options.Add(format);
        command.SetAction(parse => Render(service.Applicable(parse.GetValue(repo)!, parse.GetValue(target) ?? [], parse.GetValue(stack)), GetFormat(parse.GetValue(format))));
        return command;
    }

    private static Command Validate(StandardsGovernanceService service)
    {
        var command = new Command("validate", "Validate standard metadata, stable rules, and conformance mappings.");
        var repo = Repo(); var strict = new Option<bool>("--strict") { Description = "Treat warnings as failures." }; var format = Format();
        command.Options.Add(repo); command.Options.Add(strict); command.Options.Add(format);
        command.SetAction(parse => Render(service.Validate(parse.GetValue(repo)!, parse.GetValue(strict)), GetFormat(parse.GetValue(format))));
        return command;
    }

    private static Command Conformance(StandardsGovernanceService service)
    {
        var command = new Command("conformance", "Show canonical rule enforcement mappings without claiming that advisory evidence proves compliance.");
        var repo = Repo(); var gaps = new Option<bool>("--gaps-only") { Description = "Show not-mapped, advisory-model, or inactive rows only." }; var format = Format();
        command.Options.Add(repo); command.Options.Add(gaps); command.Options.Add(format);
        command.SetAction(parse => Render(service.Conformance(parse.GetValue(repo)!, parse.GetValue(gaps)), GetFormat(parse.GetValue(format))));
        return command;
    }

    private static Command Import(StandardImportService service)
    {
        var command = new Command("import", "Discover, validate, and import standards from local paths, GitHub repositories, or ZIP URLs.");
        var repo = Repo(); var source = new Option<string[]>("--source") { Description = "Local Markdown file/directory, ZIP, GitHub repository/tree URL, or direct HTTP(S) ZIP URL.", Arity = ArgumentArity.OneOrMore, AllowMultipleArgumentsPerToken = true, Required = true };
        var dryRun = new Option<bool>("--dry-run") { Description = "Download, discover, validate, and report the canonical import plan without changing the repository." };
        var yes = new Option<bool>("--yes") { Description = "Confirm standard files, catalog entries, and manual-review conformance rows." };
        var fix = new Option<bool>("--fix") { Description = "Repair safe metadata and section structure in staged copies; source content is not modified and rule semantics are never generated." };
        var strict = new Option<bool>("--strict") { Description = "Reject import warnings, including inferred or repaired metadata." }; var format = Format();
        command.Options.Add(repo); command.Options.Add(source); command.Options.Add(dryRun); command.Options.Add(yes); command.Options.Add(fix); command.Options.Add(strict); command.Options.Add(format);
        command.SetAction(parse => Render(service.Import(new StandardImportRequest(parse.GetValue(repo)!, parse.GetValue(source) ?? [], parse.GetValue(dryRun), parse.GetValue(yes), parse.GetValue(fix), parse.GetValue(strict))), GetFormat(parse.GetValue(format))));
        return command;
    }

    private static Command Audit(StandardAuditService service)
    {
        var command = new Command("audit", "Isolate duplicate, overlapping, and conflicting standards using deterministic and local-first semantic review.");
        var repo = Repo(); var strict = new Option<bool>("--strict") { Description = "Return a failing exit code for every unresolved finding." };
        var noLlm = new Option<bool>("--no-llm") { Description = "Run deterministic auditing only and skip automatic local or configured remote model review." };
        var fix = new Option<bool>("--fix") { Description = "Move deterministic duplicate copies and evidence-backed conflicts to standards-quarantine while preserving catalog and conformance history." };
        var model = new Option<string?>("--model") { Description = "Optional model name; routing prefers an available local provider, then a configured remote provider." };
        var maxPairs = new Option<int>("--max-pairs") { Description = "Maximum candidate pairs submitted individually for semantic review, from 1 to 1000.", DefaultValueFactory = _ => 100 };
        var format = Format(); command.Options.Add(repo); command.Options.Add(strict); command.Options.Add(noLlm); command.Options.Add(fix); command.Options.Add(model); command.Options.Add(maxPairs); command.Options.Add(format);
        command.SetAction(parse => Render(service.Audit(new StandardAuditRequest(parse.GetValue(repo)!, parse.GetValue(strict), parse.GetValue(noLlm), parse.GetValue(fix), parse.GetValue(model), parse.GetValue(maxPairs))), GetFormat(parse.GetValue(format))));
        return command;
    }

    private static Command Patterns(StandardPatternCatalogService service)
    {
        var command = new Command("patterns", "Inventory and validate built-in, extension, and repository-owned standard-inference patterns.");
        var repo = Repo(); var language = Many("--language", "Compiler language filter; repeat for more than one.");
        var applicable = new Option<bool>("--applicable") { Description = "Return only patterns applicable to classified repository components." };
        var strict = new Option<bool>("--strict") { Description = "Treat pattern-catalog warnings as failures." }; var format = Format();
        command.Options.Add(repo); command.Options.Add(language); command.Options.Add(applicable); command.Options.Add(strict); command.Options.Add(format);
        command.SetAction(parse => Render(service.Inventory(new StandardPatternCatalogRequest(parse.GetValue(repo)!, parse.GetValue(language) ?? [], parse.GetValue(applicable), parse.GetValue(strict))), GetFormat(parse.GetValue(format))));
        return command;
    }

    private static Command Infer(StandardInferenceService service)
    {
        var command = new Command("infer", "Match applicable known graph patterns and persist non-canonical inferred-standard candidates.");
        var repo = Repo(); var pattern = Many("--pattern", "Exact pattern ID; repeat to evaluate a bounded subset.");
        var language = Many("--language", "Compiler language filter; repeat for more than one.");
        var component = Many("--component", "Exact component ID; repeat to bound eligible subject nodes.");
        var evidenceLimit = new Option<int>("--evidence-limit") { Description = "Maximum match and counterexample evidence rows per pattern, from 1 to 100.", DefaultValueFactory = _ => 20 };
        var format = Format(); command.Options.Add(repo); command.Options.Add(pattern); command.Options.Add(language); command.Options.Add(component); command.Options.Add(evidenceLimit); command.Options.Add(format);
        command.SetAction(parse => Render(service.Infer(new StandardInferenceRequest(parse.GetValue(repo)!, parse.GetValue(pattern) ?? [], parse.GetValue(language) ?? [], parse.GetValue(component) ?? [], parse.GetValue(evidenceLimit))), GetFormat(parse.GetValue(format))));
        return command;
    }

    private static Option<string> Repo() => new("--repo") { Description = "Repository path. Defaults to the current directory.", DefaultValueFactory = _ => Directory.GetCurrentDirectory() };
    private static Option<string[]> Many(string name, string description) => new(name) { Description = description, AllowMultipleArgumentsPerToken = true };
    private static Option<string> Format() => new("--format") { Description = "Output format: human, json, or agent.", DefaultValueFactory = _ => "human" };

    private static string? GetFormat(string? value)
    {
        var format = (value ?? "human").ToLowerInvariant();
        if (Formats.Contains(format, StringComparer.Ordinal)) return format;
        Console.Error.WriteLine($"Unsupported format '{format}'. Expected human, json, or agent."); return null;
    }

    private static int Render(object result, string? format)
    {
        if (format is null) return 2;
        var exit = result switch { StandardInventoryResult value => value.ExitCode, StandardsValidationResult value => value.ExitCode, StandardsConformanceResult value => value.ExitCode, StandardImportResult value => value.ExitCode, StandardAuditResult value => value.ExitCode, StandardPatternCatalogResult value => value.ExitCode, StandardInferenceResult value => value.ExitCode, _ => 5 };
        if (format == "json") Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        else if (result is StandardInventoryResult inventory) RenderInventory(inventory, format);
        else if (result is StandardsValidationResult validation) RenderValidation(validation, format);
        else if (result is StandardsConformanceResult conformance) RenderConformance(conformance, format);
        else if (result is StandardImportResult import) RenderImport(import, format);
        else if (result is StandardAuditResult audit) RenderAudit(audit, format);
        else if (result is StandardPatternCatalogResult patterns) RenderPatterns(patterns, format);
        else if (result is StandardInferenceResult inference) RenderInference(inference, format);
        return exit;
    }

    private static void RenderInventory(StandardInventoryResult result, string format)
    {
        if (format == "agent") Console.WriteLine($"status={result.Status};exitCode={result.ExitCode};standards={result.Standards.Count};warnings={result.Warnings.Count};errors={result.Errors.Count}");
        else Console.WriteLine($"Standards inventory: {result.Status} ({result.Standards.Count})");
        foreach (var item in result.Standards)
            Console.WriteLine(format == "agent" ? $"standard={item.Id};status={item.Status};targets={string.Join(',', item.Targets)};stacks={string.Join(',', item.Stacks)};rules={item.RuleIds.Count};path={item.Path}" : $"{item.Id} [{item.Status}] targets={string.Join(", ", item.Targets)} stacks={string.Join(", ", item.Stacks.DefaultIfEmpty("generic"))} rules={item.RuleIds.Count} - {item.Path}");
        Messages(result.Warnings, result.Errors, format);
    }

    private static void RenderValidation(StandardsValidationResult result, string format)
    {
        if (format == "agent") Console.WriteLine($"status={result.Status};exitCode={result.ExitCode};standards={result.Standards};rules={result.Rules};conformance={result.ConformanceEntries};warnings={result.Warnings};errors={result.Errors};strict={result.Strict.ToString().ToLowerInvariant()}");
        else Console.WriteLine($"Standards validation: {result.Status}; standards={result.Standards}, rules={result.Rules}, conformance={result.ConformanceEntries}");
        foreach (var item in result.Diagnostics) Console.WriteLine(format == "agent" ? $"diagnostic={item.Code};severity={item.Severity};path={item.Path};message={item.Message};remediation={item.Remediation}" : $"{item.Severity.ToUpperInvariant()} {item.Code} {item.Path}: {item.Message} Fix: {item.Remediation}");
    }

    private static void RenderConformance(StandardsConformanceResult result, string format)
    {
        if (format == "agent") Console.WriteLine($"status={result.Status};exitCode={result.ExitCode};entries={result.Entries.Count};warnings={result.Warnings.Count};errors={result.Errors.Count}");
        else Console.WriteLine($"Standards conformance: {result.Status} ({result.Entries.Count})");
        foreach (var item in result.Entries) Console.WriteLine(format == "agent" ? $"mapping={item.StandardId};rule={item.RuleId};enforcement={item.Enforcement};surface={item.GovernedSurface};status={item.Status};evidence={item.Evidence}" : $"{item.RuleId} [{item.Enforcement}, {item.Status}] {item.GovernedSurface} - {item.Evidence}");
        Messages(result.Warnings, result.Errors, format);
    }

    private static void RenderImport(StandardImportResult result, string format)
    {
        if (format == "agent") Console.WriteLine($"status={result.Status};exitCode={result.ExitCode};standards={result.Standards.Count};dryRun={result.DryRun.ToString().ToLowerInvariant()};confirmationRequired={result.ConfirmationRequired.ToString().ToLowerInvariant()};applied={result.Applied.ToString().ToLowerInvariant()};warnings={result.Warnings.Count};conflicts={result.Conflicts.Count};errors={result.Errors.Count}");
        else Console.WriteLine($"Standards import: {result.Status} ({result.Standards.Count})");
        foreach (var item in result.Standards) Console.WriteLine(format == "agent" ? $"standard={item.Id};status={item.Status};source={Normalize(item.Source)};destination={item.Destination};hash={item.Hash};rules={item.RuleCount}" : $"{item.Id} [{item.Status}] {item.Source} -> {item.Destination} ({item.RuleCount} rules)");
        Messages(result.Warnings, result.Errors.Concat(result.Conflicts), format);
    }

    private static void RenderAudit(StandardAuditResult result, string format)
    {
        if (format == "agent") Console.WriteLine($"status={result.Status};exitCode={result.ExitCode};standards={result.StandardCount};candidatePairs={result.CandidatePairCount};duplicates={result.DuplicateCount};overlaps={result.OverlapCount};conflicts={result.ConflictCount};llmStatus={result.LlmStatus};provider={Normalize(result.Provider)};model={Normalize(result.Model)};fixRequested={result.FixRequested.ToString().ToLowerInvariant()};applied={result.Applied.ToString().ToLowerInvariant()};quarantined={result.QuarantinedStandards.Count}");
        else Console.WriteLine($"Standards audit: {result.Status}; standards={result.StandardCount}, duplicates={result.DuplicateCount}, overlaps={result.OverlapCount}, conflicts={result.ConflictCount}, llm={result.LlmStatus}");
        foreach (var item in result.Findings) Console.WriteLine(format == "agent" ? $"finding={item.Id};kind={item.Kind};severity={item.Severity};standards={string.Join(',', item.Standards)};method={item.Method};confidence={item.Confidence:0.00};summary={Normalize(item.Summary)}" : $"{item.Kind.ToUpperInvariant()} {string.Join(" / ", item.Standards)} [{item.Method}, {item.Confidence:0.00}] {item.Summary}");
        if (format == "agent") { Console.WriteLine($"jsonPath={Normalize(result.JsonPath)}"); Console.WriteLine($"markdownPath={Normalize(result.MarkdownPath)}"); Console.WriteLine($"quarantinePath={Normalize(result.QuarantinePath)}"); foreach (var id in result.QuarantinedStandards) Console.WriteLine($"quarantinedStandard={id}"); }
        Messages(result.Warnings, result.Errors, format);
    }

    private static void RenderPatterns(StandardPatternCatalogResult result, string format)
    {
        if (format == "agent") Console.WriteLine($"status={result.Status};exitCode={result.ExitCode};providers={result.ProviderCount};patterns={result.Patterns.Count};ready={result.Patterns.Count(item => item.Readiness == "ready")};applicable={result.Patterns.Count(item => item.Applicable)};warnings={result.Warnings.Count};errors={result.Errors.Count}");
        else Console.WriteLine($"Standard patterns: {result.Status}; providers={result.ProviderCount}, patterns={result.Patterns.Count}");
        foreach (var item in result.Patterns) Console.WriteLine(format == "agent"
            ? $"pattern={item.Definition.Id};version={item.Definition.Version};status={item.Definition.Status};provider={Normalize(item.Definition.Provider)};authority={item.Definition.Authority};languages={string.Join(',', item.Definition.Languages)};applicable={item.Applicable.ToString().ToLowerInvariant()};readiness={item.Readiness};missing={string.Join(',', item.MissingGraphCapabilities)};source={Normalize(item.Definition.Source)}"
            : $"{item.Definition.Id}@{item.Definition.Version} [{item.Definition.Status}, {item.Readiness}] {item.Definition.Title}");
        foreach (var item in result.Diagnostics) Console.WriteLine(format == "agent" ? $"diagnostic={item.Code};severity={item.Severity};pattern={item.PatternId};source={Normalize(item.Source)};message={Normalize(item.Message)};remediation={Normalize(item.Remediation)}" : $"{item.Severity.ToUpperInvariant()} {item.Code} {item.PatternId}: {item.Message} Fix: {item.Remediation}");
    }

    private static void RenderInference(StandardInferenceResult result, string format)
    {
        if (format == "agent") Console.WriteLine($"status={result.Status};exitCode={result.ExitCode};graphBuild={Normalize(result.GraphBuildId)};freshness={result.Freshness};patterns={result.PatternCount};evaluated={result.EvaluatedCount};candidates={result.CandidateCount}");
        else Console.WriteLine($"Standards inference: {result.Status}; patterns={result.PatternCount}, evaluated={result.EvaluatedCount}, candidates={result.CandidateCount}");
        foreach (var item in result.Evaluations) Console.WriteLine(format == "agent" ? $"evaluation={item.PatternId};version={item.Version};status={item.Status};eligible={item.EligibleOccurrences};matches={item.MatchingOccurrences};consistency={item.Consistency:0.000};missing={string.Join(',', item.MissingGraphCapabilities)}" : $"{item.PatternId}: {item.Status}; {item.MatchingOccurrences}/{item.EligibleOccurrences} ({item.Consistency:P1})");
        foreach (var item in result.Candidates) Console.WriteLine(format == "agent" ? $"candidate={item.Id};pattern={item.PatternId};status={item.Status};target={item.Target};level={Normalize(item.ProposedLevel)};consistency={item.Consistency:0.000};wording={Normalize(item.ProposedWording)}" : $"{item.Id} [{item.ProposedLevel}] {item.ProposedWording}");
        if (format == "agent") { Console.WriteLine($"jsonPath={Normalize(result.JsonPath)}"); Console.WriteLine($"markdownPath={Normalize(result.MarkdownPath)}"); }
        Messages(result.Warnings, result.Errors, format);
    }

    private static string Normalize(string? value) => (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Replace(';', ',');

    private static void Messages(IEnumerable<string> warnings, IEnumerable<string> errors, string format)
    {
        foreach (var warning in warnings) Console.WriteLine(format == "agent" ? $"warning={warning}" : $"Warning: {warning}");
        foreach (var error in errors) Console.WriteLine(format == "agent" ? $"error={error}" : $"Error: {error}");
    }
}

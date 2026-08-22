using System.CommandLine;
using System.Text.Json;
using Cis.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Cis.Modules.Skills;

public sealed class SkillsModule : ICisModule
{
    private static readonly string[] SupportedFormats = ["human", "json", "agent"];

    public string Name => "skills";

    public string Description => "Inventory and validate portable implementation skills.";

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<SkillValidationService>();
        services.AddSingleton<SkillImportService>();
        services.AddSingleton(provider => new SkillAuditService(
            provider.GetRequiredService<ICisRepositoryContextResolver>(),
            provider.GetRequiredService<SkillValidationService>(),
            provider.GetService<ICisTextGenerationService>()));
        services.AddSingleton<ICisRepositoryDoctorCheck, SkillDoctorCheck>();
    }

    public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services)
    {
        var service = services.GetRequiredService<SkillValidationService>();
        var savings = services.GetService<ICisTokenSavingsCollector>();
        var skills = new Command(Name, Description);
        skills.Subcommands.Add(CreateInventoryCommand(service, savings));
        skills.Subcommands.Add(CreateValidateCommand(service, savings));
        skills.Subcommands.Add(CreateImportCommand(services.GetRequiredService<SkillImportService>()));
        skills.Subcommands.Add(CreateAuditCommand(services.GetRequiredService<SkillAuditService>()));
        commands.Add(skills);
    }

    private static Command CreateAuditCommand(SkillAuditService service)
    {
        var command = new Command("audit", "Isolate duplicate, overlapping, and conflicting repository skills using deterministic and local-first model review.");
        var repo = CreateRepositoryOption();
        var format = CreateFormatOption();
        var strict = new Option<bool>("--strict")
        {
            Description = "Return a failing exit code for any unresolved deterministic or model audit finding.",
        };
        var noLlm = new Option<bool>("--no-llm")
        {
            Description = "Run deterministic auditing only and skip automatic local or remote model review.",
        };
        var fix = new Option<bool>("--fix")
        {
            Description = "Move redundant duplicate copies and both sides of evidence-backed conflicts into .github/skills-quarantine/.",
        };
        var model = new Option<string?>("--model")
        {
            Description = "Optional model name. Provider selection prefers an available local provider, then a configured remote provider.",
        };
        var maximumPairs = new Option<int>("--max-pairs")
        {
            Description = "Maximum bounded candidate pairs submitted for semantic review, from 1 to 1000.",
            DefaultValueFactory = _ => 100,
        };
        command.Options.Add(repo);
        command.Options.Add(strict);
        command.Options.Add(noLlm);
        command.Options.Add(fix);
        command.Options.Add(model);
        command.Options.Add(maximumPairs);
        command.Options.Add(format);
        command.SetAction(parseResult =>
        {
            var selectedFormat = GetFormat(parseResult.GetValue(format));
            if (selectedFormat is null)
            {
                return 2;
            }

            var result = service.Audit(new SkillAuditRequest(
                parseResult.GetValue(repo) ?? Directory.GetCurrentDirectory(),
                parseResult.GetValue(strict),
                parseResult.GetValue(noLlm),
                parseResult.GetValue(fix),
                parseResult.GetValue(model),
                parseResult.GetValue(maximumPairs)));
            RenderAudit(result, selectedFormat);
            return result.ExitCode;
        });
        return command;
    }

    private static Command CreateImportCommand(SkillImportService service)
    {
        var command = new Command("import", "Discover, validate, and import skill bundles from local paths, GitHub repositories, or ZIP URLs.");
        var repo = CreateRepositoryOption();
        var format = CreateFormatOption();
        var source = new Option<string[]>("--source")
        {
            Description = "Local path, GitHub repository/tree URL, or direct HTTP(S) ZIP URL. Repeat or provide several values.",
            Arity = ArgumentArity.OneOrMore,
            AllowMultipleArgumentsPerToken = true,
            Required = true,
        };
        var dryRun = new Option<bool>("--dry-run")
        {
            Description = "Download, discover, validate, and report the import plan without changing the repository.",
        };
        var yes = new Option<bool>("--yes")
        {
            Description = "Confirm creation of every planned repository skill bundle.",
        };
        var fix = new Option<bool>("--fix")
        {
            Description = "Repair missing YAML fields or headings in staged copies; source content is not modified.",
        };
        var strict = new Option<bool>("--strict")
        {
            Description = "Reject imported skills that produce validation warnings.",
        };
        command.Options.Add(repo);
        command.Options.Add(source);
        command.Options.Add(dryRun);
        command.Options.Add(yes);
        command.Options.Add(fix);
        command.Options.Add(strict);
        command.Options.Add(format);
        command.SetAction(parseResult =>
        {
            var selectedFormat = GetFormat(parseResult.GetValue(format));
            if (selectedFormat is null)
            {
                return 2;
            }

            var result = service.Import(new SkillImportRequest(
                parseResult.GetValue(repo) ?? Directory.GetCurrentDirectory(),
                parseResult.GetValue(source) ?? [],
                parseResult.GetValue(dryRun),
                parseResult.GetValue(yes),
                parseResult.GetValue(fix),
                parseResult.GetValue(strict)));
            RenderImport(result, selectedFormat);
            return result.ExitCode;
        });
        return command;
    }

    private static Command CreateInventoryCommand(SkillValidationService service, ICisTokenSavingsCollector? savings)
    {
        var command = new Command("inventory", "List repository skills and report structural errors.");
        var repo = CreateRepositoryOption();
        var format = CreateFormatOption();
        var details = new Option<bool>("--details") { Description = "Include every valid skill in agent output." };
        command.Options.Add(repo);
        command.Options.Add(format);
        command.Options.Add(details);
        command.SetAction(parseResult =>
        {
            var selectedFormat = GetFormat(parseResult.GetValue(format));
            if (selectedFormat is null)
            {
                return 2;
            }

            var result = service.Validate(
                parseResult.GetValue(repo) ?? Directory.GetCurrentDirectory(),
                strict: false);
            AddCompactSavings(result, selectedFormat, parseResult.GetValue(details), savings);
            Render(result, selectedFormat, includeDiagnostics: false, parseResult.GetValue(details));
            return result.ErrorCount > 0 ? 2 : 0;
        });
        return command;
    }

    private static Command CreateValidateCommand(SkillValidationService service, ICisTokenSavingsCollector? savings)
    {
        var command = new Command("validate", "Validate skill names, metadata, bodies, and local resource links.");
        var repo = CreateRepositoryOption();
        var format = CreateFormatOption();
        var strict = new Option<bool>("--strict")
        {
            Description = "Treat skill validation warnings as failures.",
        };
        var fix = new Option<bool>("--fix")
        {
            Description = "Safely add missing YAML fields and level-one headings before validation.",
        };
        var details = new Option<bool>("--details") { Description = "Include every valid skill in agent output." };
        command.Options.Add(repo);
        command.Options.Add(format);
        command.Options.Add(strict);
        command.Options.Add(fix);
        command.Options.Add(details);
        command.SetAction(parseResult =>
        {
            var selectedFormat = GetFormat(parseResult.GetValue(format));
            if (selectedFormat is null)
            {
                return 2;
            }

            var result = service.Validate(
                parseResult.GetValue(repo) ?? Directory.GetCurrentDirectory(),
                parseResult.GetValue(strict),
                parseResult.GetValue(fix));
            AddCompactSavings(result, selectedFormat, parseResult.GetValue(details), savings);
            Render(result, selectedFormat, includeDiagnostics: true, parseResult.GetValue(details));
            return result.ExitCode;
        });
        return command;
    }

    private static Option<string> CreateRepositoryOption()
        => new("--repo")
        {
            Description = "Repository path. Defaults to the current directory.",
            DefaultValueFactory = _ => Directory.GetCurrentDirectory(),
        };

    private static Option<string> CreateFormatOption()
        => new("--format")
        {
            Description = "Output format: human, json, or agent.",
            DefaultValueFactory = _ => "human",
        };

    private static string? GetFormat(string? format)
    {
        var selected = (format ?? "human").ToLowerInvariant();
        if (SupportedFormats.Contains(selected, StringComparer.Ordinal))
        {
            return selected;
        }

        Console.Error.WriteLine($"Unsupported format '{selected}'. Expected human, json, or agent.");
        return null;
    }

    private static void Render(SkillValidationResult result, string format, bool includeDiagnostics, bool details = false)
    {
        if (format == "json")
        {
            Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
            }));
            return;
        }

        if (format == "agent")
        {
            Console.WriteLine($"status={result.Status};exitCode={result.ExitCode};skills={result.SkillCount};errors={result.ErrorCount};warnings={result.WarningCount};strict={result.Strict.ToString().ToLowerInvariant()};fixRequested={result.FixRequested.ToString().ToLowerInvariant()};applied={result.Applied.ToString().ToLowerInvariant()};fixed={result.FixedPaths.Count}");
            Console.WriteLine($"repository={Normalize(result.RepositoryPath)}");
            foreach (var path in result.FixedPaths)
            {
                Console.WriteLine($"fixedPath={Normalize(path)}");
            }
            foreach (var skill in details ? result.Skills : [])
            {
                Console.WriteLine($"skill={Normalize(skill.Name)};path={Normalize(skill.Path)};lines={skill.LineCount};description={Normalize(skill.Description)}");
            }

            if (includeDiagnostics)
            {
                foreach (var diagnostic in result.Diagnostics)
                {
                    Console.WriteLine($"diagnostic={diagnostic.Code};severity={diagnostic.Severity};path={Normalize(diagnostic.Path)};message={Normalize(diagnostic.Message)}");
                }
            }

            return;
        }

        Console.WriteLine($"Skill validation: {result.Status}");
        Console.WriteLine($"Repository: {result.RepositoryPath ?? string.Empty}");
        Console.WriteLine($"Skills: {result.SkillCount}; errors: {result.ErrorCount}; warnings: {result.WarningCount}");
        foreach (var path in result.FixedPaths)
        {
            Console.WriteLine($"Fixed: {path}");
        }
        foreach (var skill in result.Skills)
        {
            Console.WriteLine($"Skill: {skill.Name} [{skill.Path}; {skill.LineCount} lines]");
            Console.WriteLine($"  {skill.Description}");
        }

        if (includeDiagnostics)
        {
            foreach (var diagnostic in result.Diagnostics)
            {
                Console.WriteLine($"{char.ToUpperInvariant(diagnostic.Severity[0]) + diagnostic.Severity[1..]} {diagnostic.Code}: {diagnostic.Message} [{diagnostic.Path}]");
                foreach (var evidence in diagnostic.Evidence)
                {
                    Console.WriteLine($"  Evidence: {evidence}");
                }
            }
        }
    }

    private static void AddCompactSavings(SkillValidationResult result, string format, bool details,
        ICisTokenSavingsCollector? savings)
    {
        if (format != "agent" || details || savings is null) return;
        var characters = 250 + result.Skills.Sum(skill =>
            skill.Name.Length + skill.Path.Length + skill.Description.Length + 45);
        characters += result.Diagnostics.Sum(item => item.Message.Length + item.Path.Length + 35);
        savings.Add(new CisTokenSavingsCandidate(
            Math.Max(1, (int)Math.Ceiling(characters / 4d)),
            null,
            "deterministic compact skills output versus the same result with --details",
            "high"));
    }

    private static void RenderImport(SkillImportResult result, string format)
    {
        if (format == "json")
        {
            Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
            }));
            return;
        }

        if (format == "agent")
        {
            Console.WriteLine($"status={result.Status};exitCode={result.ExitCode};skills={result.Skills.Count};dryRun={result.DryRun.ToString().ToLowerInvariant()};confirmationRequired={result.ConfirmationRequired.ToString().ToLowerInvariant()};applied={result.Applied.ToString().ToLowerInvariant()};warnings={result.Warnings.Count};conflicts={result.Conflicts.Count};errors={result.Errors.Count}");
            Console.WriteLine($"repository={Normalize(result.RepositoryPath)}");
            foreach (var skill in result.Skills)
            {
                Console.WriteLine($"skill={Normalize(skill.Name)};status={skill.Status};source={Normalize(skill.Source)};destination={Normalize(skill.Destination)};hash={skill.Hash};files={skill.FileCount}");
            }

            RenderAgentItems("warning", result.Warnings);
            RenderAgentItems("conflict", result.Conflicts);
            RenderAgentItems("error", result.Errors);
            return;
        }

        Console.WriteLine($"Skill import: {result.Status}");
        Console.WriteLine($"Repository: {result.RepositoryPath ?? string.Empty}");
        foreach (var skill in result.Skills)
        {
            Console.WriteLine($"Skill: {skill.Name} [{skill.Status}] {skill.Source} -> {skill.Destination} ({skill.FileCount} files)");
        }

        foreach (var warning in result.Warnings)
        {
            Console.WriteLine($"Warning: {warning}");
        }

        foreach (var conflict in result.Conflicts)
        {
            Console.WriteLine($"Conflict: {conflict}");
        }

        foreach (var error in result.Errors)
        {
            Console.WriteLine($"Error: {error}");
        }

        if (result.ConfirmationRequired)
        {
            Console.WriteLine("No files were changed. Review the plan and rerun with --yes to import.");
        }
    }

    private static void RenderAudit(SkillAuditResult result, string format)
    {
        if (format == "json")
        {
            Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
            }));
            return;
        }

        if (format == "agent")
        {
            Console.WriteLine($"status={result.Status};exitCode={result.ExitCode};skills={result.SkillCount};candidatePairs={result.CandidatePairCount};duplicates={result.DuplicateCount};overlaps={result.OverlapCount};conflicts={result.ConflictCount};llmStatus={result.LlmStatus};provider={Normalize(result.Provider)};model={Normalize(result.Model)};fixRequested={result.FixRequested.ToString().ToLowerInvariant()};applied={result.Applied.ToString().ToLowerInvariant()};quarantined={result.QuarantinedSkills.Count}");
            Console.WriteLine($"repository={Normalize(result.RepositoryPath)}");
            Console.WriteLine($"jsonPath={Normalize(result.JsonPath)}");
            Console.WriteLine($"markdownPath={Normalize(result.MarkdownPath)}");
            Console.WriteLine($"quarantinePath={Normalize(result.QuarantinePath)}");
            foreach (var skill in result.QuarantinedSkills)
            {
                Console.WriteLine($"quarantinedSkill={Normalize(skill)}");
            }
            foreach (var finding in result.Findings)
            {
                Console.WriteLine($"finding={finding.Id};kind={finding.Kind};severity={finding.Severity};skills={Normalize(string.Join(',', finding.Skills))};method={finding.Method};confidence={finding.Confidence:0.00};summary={Normalize(finding.Summary)}");
            }
            RenderAgentItems("warning", result.Warnings);
            RenderAgentItems("error", result.Errors);
            return;
        }

        Console.WriteLine($"Skill audit: {result.Status}");
        Console.WriteLine($"Repository: {result.RepositoryPath ?? string.Empty}");
        Console.WriteLine($"Skills: {result.SkillCount}; candidates: {result.CandidatePairCount}; duplicates: {result.DuplicateCount}; overlaps: {result.OverlapCount}; conflicts: {result.ConflictCount}");
        Console.WriteLine($"LLM: {result.LlmStatus}{(result.Provider is null ? string.Empty : $" ({result.Provider}/{result.Model})")}");
        if (result.QuarantinedSkills.Count > 0)
        {
            Console.WriteLine($"Quarantined: {string.Join(", ", result.QuarantinedSkills)} -> {result.QuarantinePath}");
        }
        foreach (var finding in result.Findings)
        {
            Console.WriteLine($"{finding.Kind}: {string.Join(", ", finding.Skills)} - {finding.Summary} [{finding.Method}; {finding.Confidence:0.00}]");
        }
        foreach (var warning in result.Warnings)
        {
            Console.WriteLine($"Warning: {warning}");
        }
        foreach (var error in result.Errors)
        {
            Console.WriteLine($"Error: {error}");
        }
        if (result.MarkdownPath is not null)
        {
            Console.WriteLine($"Report: {result.MarkdownPath}");
        }
    }

    private static void RenderAgentItems(string kind, IEnumerable<string> values)
    {
        foreach (var value in values)
        {
            Console.WriteLine($"{kind}={Normalize(value)}");
        }
    }

    private static string Normalize(string? value)
        => (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Replace(';', ',').Trim();
}

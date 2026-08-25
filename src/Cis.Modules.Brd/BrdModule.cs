using System.CommandLine;
using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Repository;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cis.Modules.Brd;

public sealed class BrdModule : ICisModule
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
        services.AddSingleton<BrdBacklogService>();
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
        brd.Subcommands.Add(CreateApproveCommand(service));
        brd.Subcommands.Add(CreateBacklogCommand(services.GetRequiredService<BrdBacklogService>()));
        brd.Subcommands.Add(CreateFeatureCommand(services.GetRequiredService<BrdBacklogService>()));
        commands.Add(brd);
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
        backlog.Subcommands.Add(CreateBacklogSimpleCommand("build", "Build or reconcile one high-level outcome per functional BRD requirement.", service.Build));
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

    private static void RenderBacklog(BrdBacklogResult result, string format)
    {
        if (format == "json") { Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions)); return; }
        if (format == "agent")
        {
            Console.WriteLine($"status={Clean(result.Status)};exitCode={result.ExitCode};applied={result.Applied.ToString().ToLowerInvariant()};items={result.Items.Count};effectiveStatus={Clean(result.Validation?.EffectiveStatus)};valid={result.Validation?.Valid.ToString().ToLowerInvariant() ?? "false"};current={result.Validation?.Current.ToString().ToLowerInvariant() ?? "false"}");
            Console.WriteLine($"workspace={Clean(result.WorkspacePath)}"); Console.WriteLine($"authority={Clean(result.AuthorityRepositoryId)}"); Console.WriteLine($"canonicalPath={Clean(result.RelativePath)}");
            foreach (var item in result.Items) Console.WriteLine($"item={Clean(item.Id)};requirement={Clean(item.RequirementId)};repositories={Clean(string.Join(',', item.Repositories))};frontendTypes={Clean(string.Join(',', item.FrontendTypes))};dependsOn={Clean(string.Join(',', item.DependsOn))};featureSpec={Clean(item.FeatureSpecification)};outcome={Clean(item.Outcome)}");
            foreach (var error in result.Validation?.Errors ?? []) Console.WriteLine($"validationError={Clean(error)}");
            foreach (var warning in result.Validation?.Warnings ?? []) Console.WriteLine($"validationWarning={Clean(warning)}");
            foreach (var error in result.Errors) Console.WriteLine($"error={Clean(error)}");
            return;
        }
        Console.WriteLine($"BRD high-level backlog: {result.Status}; items: {result.Items.Count}");
        Console.WriteLine($"Effective status: {result.Validation?.EffectiveStatus ?? string.Empty}");
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

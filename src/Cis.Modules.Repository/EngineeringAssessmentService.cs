using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Repository;

/// <summary>Defines what must be assessed. Result bindings and test passes cannot reduce this inventory.</summary>
public sealed class EngineeringAssessmentService : ICisEngineeringAssessment
{
    public const string PolicyPath = ".cis/engineering-defaults.json";
    private static readonly HashSet<string> NonImplementationCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "documentation", "coordination", "wireframe", "design", "decision", "discovery", "requirements",
    };

    public EngineeringAssessment Assess(CisRepositoryContext context, string taskCategory)
    {
        var diagnostics = new List<string>();
        var policy = ReadPolicy(context.RepositoryPath, diagnostics);
        if (policy is null) return new(1, diagnostics.Count > 0, [], diagnostics);
        if (!policy.RequireTaskCompletion) return new(1, false, [], []);
        var requirements = new List<EngineeringGateRequirement>
        {
            new("graph", "Final-state graph and explicit extraction limitations", false),
            new("alignment", "Reassess all standards, CIS definitions, skills and adopted configuration", false),
            new("context", "Source-backed callers, contracts and operational context; material omissions resolved", false),
            new("traceability", "Current scoped requirements, implementation and verification mappings", false),
            new("review", "Assigned independent reviewer checks final-state logic and closing evidence", false),
        };
        var classification = new RepositoryClassifier().Classify(context.RepositoryPath);
        if (classification.Warnings.Count > 0)
            requirements.Add(new("configuration-review", "Static classification is incomplete: " + string.Join("; ", classification.Warnings), false));
        var implementation = classification.Components.Any(component => component.Roles.Any(role => role != "test-automation"));
        if (implementation && !NonImplementationCategories.Contains(taskCategory))
        {
            foreach (var id in new[] { "build", "unit", "component", "coverage", "mutation", "architecture", "lint", "format",
                         "integration", "api-compatibility", "regression", "business", "browser", "security", "instrumentation",
                         "performance", "cli", "skills" })
                requirements.Add(new(id, "Assess changed behavior, requirements, risk and adopted standards; preserve stronger project gates", id is not ("build" or "unit")));
        }
        foreach (var id in policy.AdditionalGates)
            if (!requirements.Any(gate => gate.Id == id)) requirements.Add(new(id, "Additional adopted project gate", false));
        return new(1, true, requirements, diagnostics);
    }

    internal static EngineeringDefaultsPolicy? ReadPolicy(string repository, ICollection<string> diagnostics)
    {
        try
        {
            return CisEngineeringPolicy.Read(repository);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        { diagnostics.Add($"Invalid engineering defaults policy: {exception.Message}"); return null; }
    }
}

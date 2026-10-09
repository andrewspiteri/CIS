using Cis.Abstractions;

namespace Cis.Modules.Repository;

/// <summary>Reuses the ownership-aware initializer as a read-only alignment assessment.</summary>
public sealed class EngineeringAlignmentDoctorCheck : ICisRepositoryDoctorCheck
{
    public string Name => "engineering-alignment";

    public IReadOnlyList<CisRepositoryDoctorFinding> Inspect(CisRepositoryContext context)
    {
        var assessment = new EngineeringAssessmentService().Assess(context, "documentation");
        if (!assessment.Adopted) return [];
        var findings = assessment.Diagnostics.Select(message => Finding("CIS-ENGINEERING-001", message, [], "Review the adopted engineering policy.")).ToList();
        var preview = new RepositoryInitializer().Initialize(new(context.RepositoryPath, context.DocumentationRoot, DryRun: true, Confirmed: false));
        if (preview.Collisions.Count > 0 || preview.Errors.Count > 0)
            findings.Add(Finding("CIS-ENGINEERING-002", "Engineering alignment has unresolved reconciliation conflicts.",
                preview.Collisions.Concat(preview.Errors).ToArray(), "Review the reconciliation preview; preserve human-owned guidance and adopted alternatives."));
        var pending = preview.FilesToCreate.Concat(preview.FilesToUpdate).ToArray();
        if (pending.Length > 0)
            findings.Add(Finding("CIS-ENGINEERING-003", "Current source or starter definitions require reviewed reconciliation across standards, profiles and skills.",
                pending, "Preview cis repo init with this repository and documentation root, apply authorized updates, then rebuild graph and affected evidence."));
        var profile = Path.Combine(context.DocumentationPath, "references", "repository-profile.md");
        if (File.Exists(profile))
        {
            var roots = ReadComponentRoots(File.ReadAllText(profile))
                .Select(relative => CisPathSafety.TryResolveUnderRoot(context.RepositoryPath, relative, out var root, allowRoot: true) ? root : null)
                .OfType<string>().ToArray();
            var unowned = new RepositoryClassifier().Classify(context.RepositoryPath).Components
                .Where(component => !roots.Any(root => OwnsComponent(context.RepositoryPath, root, component.Root)))
                .Select(component => component.Id + ": " + component.Root).ToArray();
            if (unowned.Length > 0)
                findings.Add(Finding("CIS-ENGINEERING-004", "Discovered components have no declared ownership root in the repository profile.",
                    unowned, "Review current classification and add the missing component ownership to repository-profile.md; retain intentional custom guidance."));
        }
        return findings;
    }

    private static bool OwnsComponent(string repository, string declaredRoot, string componentRoot)
    {
        var componentPath = Path.GetFullPath(Path.Combine(repository, componentRoot));
        // Root-level infrastructure must not silently adopt every project added later.
        if (!CisPathSafety.IsUnderRoot(repository, declaredRoot))
            return CisPathSafety.IsUnderRoot(componentPath, declaredRoot, allowRoot: true);
        return CisPathSafety.IsUnderRoot(declaredRoot, componentPath, allowRoot: true);
    }

    private static IEnumerable<string> ReadComponentRoots(string content)
    {
        var components = new List<(string Id, string Root)>();
        string? id = null;
        var root = string.Empty;
        foreach (var rawLine in content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                if (id is not null) components.Add((id, root));
                id = line[4..].Trim();
                root = string.Empty;
            }
            else if (id is not null && line.StartsWith("- Root:", StringComparison.OrdinalIgnoreCase))
            {
                root = line[7..].Trim();
                if (root.Length >= 2 && root[0] == '`' && root[^1] == '`') root = root[1..^1];
            }
        }
        if (id is not null) components.Add((id, root));
        return components.Where(component => !string.IsNullOrWhiteSpace(component.Id))
            .GroupBy(component => component.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First().Root).Where(value => !string.IsNullOrWhiteSpace(value));
    }

    private static CisRepositoryDoctorFinding Finding(string code, string message, IReadOnlyList<string> evidence, string fix)
        => new(code, "error", "engineering", message, evidence, fix, null, "review-required");
}

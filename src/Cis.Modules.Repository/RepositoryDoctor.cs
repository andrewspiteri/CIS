using Cis.Abstractions;

namespace Cis.Modules.Repository;

public sealed class RepositoryDoctor
{
    private readonly IReadOnlyList<ICisRepositoryDoctorCheck> _checks;
    private readonly RepositoryInitializer _initializer;
    private readonly IOllamaProbe _ollamaProbe;
    private readonly ICisRepositoryContextResolver _repositoryContextResolver;

    public RepositoryDoctor(
        ICisRepositoryContextResolver repositoryContextResolver,
        RepositoryInitializer initializer,
        IEnumerable<ICisRepositoryDoctorCheck> checks,
        IOllamaProbe ollamaProbe)
    {
        _repositoryContextResolver = repositoryContextResolver;
        _initializer = initializer;
        _checks = checks.OrderBy(check => check.Name, StringComparer.Ordinal).ToArray();
        _ollamaProbe = ollamaProbe;
    }

    public RepositoryDoctorResult Inspect(string repositoryPath, string? documentationRoot = null)
    {
        var ollama = _ollamaProbe.Probe();
        var findings = new List<CisRepositoryDoctorFinding>();
        AddOllamaFinding(ollama, findings);

        var resolution = _repositoryContextResolver.Resolve(repositoryPath);
        if (!resolution.IsSuccess)
        {
            if (!string.IsNullOrWhiteSpace(documentationRoot))
            {
                AddReconciliationFindings(repositoryPath, documentationRoot, findings);
            }

            findings.Add(new CisRepositoryDoctorFinding(
                "CIS-REPO-001",
                "error",
                "configuration",
                "The repository is not initialized with a valid CIS configuration.",
                resolution.Errors,
                "Initialize the repository with an explicit documentation root, or correct .cis/repository.yml.",
                "cis repo init --root <documentation-root> --dry-run",
                "review-required"));
            return CreateResult(
                TryResolveRepositoryPath(repositoryPath),
                documentationRoot,
                ollama,
                findings,
                repositoryConfigurationValid: false);
        }

        var context = resolution.Context!;
        AddReconciliationFindings(context.RepositoryPath, context.DocumentationRoot, findings);
        RunContributedChecks(context, findings);
        return CreateResult(
            context.RepositoryPath,
            context.DocumentationRoot,
            ollama,
            findings,
            repositoryConfigurationValid: true);
    }

    private void AddReconciliationFindings(
        string repositoryPath,
        string documentationRoot,
        ICollection<CisRepositoryDoctorFinding> findings)
    {
        var result = _initializer.Initialize(new RepositoryInitRequest(
            repositoryPath,
            documentationRoot,
            DryRun: true,
            Confirmed: false));

        foreach (var error in result.Errors)
        {
            findings.Add(new CisRepositoryDoctorFinding(
                "CIS-REPO-002",
                "error",
                "initialization",
                error,
                [],
                "Correct the repository initialization error and rerun doctor.",
                $"cis repo init --root {documentationRoot} --dry-run",
                "review-required"));
        }

        foreach (var collision in result.Collisions)
        {
            findings.Add(new CisRepositoryDoctorFinding(
                "CIS-REPO-003",
                "error",
                "managed-content",
                collision,
                [],
                "Review and reconcile the generated baseline. If the current file is canonical, accept it as human-owned.",
                $"cis repo init --root {documentationRoot} --accept-current --yes",
                "manual"));
        }

        foreach (var warning in result.Warnings)
        {
            var obsoleteManagedArtifact = warning.StartsWith(
                "Previously managed artifact is no longer selected",
                StringComparison.Ordinal);
            findings.Add(new CisRepositoryDoctorFinding(
                "CIS-REPO-004",
                "warning",
                "classification",
                warning,
                [],
                obsoleteManagedArtifact
                    ? "Review the obsolete artifact and preview recoverable quarantine; edited or human-owned content will remain in place."
                    : "Review the classification evidence and correct the repository structure or classification when needed.",
                obsoleteManagedArtifact
                    ? $"cis repo init --root {documentationRoot} --quarantine-obsolete --dry-run"
                    : $"cis repo init --root {documentationRoot} --dry-run",
                "review-required"));
        }

        var delta = result.FilesToCreate
            .Select(path => "create: " + path)
            .Concat(result.FilesToUpdate.Select(path => "update: " + path))
            .ToArray();
        if (delta.Length > 0)
        {
            findings.Add(new CisRepositoryDoctorFinding(
                "CIS-REPO-005",
                "warning",
                "reconciliation",
                $"Repository initialization has {delta.Length} unapplied artifact change(s).",
                LimitEvidence(delta),
                "Review the initialization plan, then apply it to reconcile CIS assets.",
                $"cis repo init --root {documentationRoot} --dry-run",
                "review-required"));
        }
    }

    private void RunContributedChecks(
        CisRepositoryContext context,
        ICollection<CisRepositoryDoctorFinding> findings)
    {
        foreach (var check in _checks)
        {
            try
            {
                foreach (var finding in check.Inspect(context))
                {
                    findings.Add(finding);
                }
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
            {
                findings.Add(new CisRepositoryDoctorFinding(
                    "CIS-CHECK-001",
                    "error",
                    "doctor-check",
                    $"Repository doctor check '{check.Name}' failed: {exception.Message}",
                    [],
                    "Correct the reported access or check configuration problem and rerun doctor.",
                    null,
                    "manual"));
            }
        }
    }

    private static void AddOllamaFinding(
        OllamaProbeResult ollama,
        ICollection<CisRepositoryDoctorFinding> findings)
    {
        if (!ollama.IsAvailable)
        {
            findings.Add(new CisRepositoryDoctorFinding(
                "CIS-OLLAMA-001",
                "warning",
                "local-llm",
                "Ollama is not available.",
                string.IsNullOrWhiteSpace(ollama.Detail)
                    ? [ollama.Endpoint]
                    : [ollama.Endpoint, ollama.Detail],
                "Install Ollama or start its local service. Set OLLAMA_HOST when it is not using the default endpoint.",
                "ollama serve",
                "manual"));
            return;
        }

        if (ollama.Models.Count == 0)
        {
            findings.Add(new CisRepositoryDoctorFinding(
                "CIS-OLLAMA-002",
                "warning",
                "local-llm",
                "Ollama is available but no local models were reported.",
                [ollama.Endpoint],
                "Pull at least one model appropriate for repository analysis.",
                "ollama pull <model>",
                "manual"));
            return;
        }

        findings.Add(new CisRepositoryDoctorFinding(
            "CIS-OLLAMA-003",
            "info",
            "local-llm",
            $"Ollama is available with {ollama.Models.Count} local model(s).",
            ollama.Models,
            "No action is required.",
            null,
            "none"));
    }

    private static RepositoryDoctorResult CreateResult(
        string? repositoryPath,
        string? documentationRoot,
        OllamaProbeResult ollama,
        IReadOnlyList<CisRepositoryDoctorFinding> findings,
        bool repositoryConfigurationValid)
    {
        var ordered = findings
            .OrderBy(finding => SeverityOrder(finding.Severity))
            .ThenBy(finding => finding.Code, StringComparer.Ordinal)
            .ThenBy(finding => finding.Message, StringComparer.Ordinal)
            .ToArray();
        var status = ordered.Any(finding => finding.Severity == "error")
            ? "errors"
            : ordered.Any(finding => finding.Severity == "warning")
                ? "warnings"
                : "healthy";
        return new RepositoryDoctorResult(
            status,
            repositoryPath,
            documentationRoot,
            ollama,
            ordered,
            repositoryConfigurationValid);
    }

    private static int SeverityOrder(string severity) => severity switch
    {
        "error" => 0,
        "warning" => 1,
        _ => 2,
    };

    private static string? TryResolveRepositoryPath(string repositoryPath)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryPath));
        }
        catch (Exception exception) when (exception is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> LimitEvidence(IReadOnlyList<string> values)
    {
        const int limit = 20;
        if (values.Count <= limit)
        {
            return values;
        }

        return values.Take(limit)
            .Append($"... and {values.Count - limit} more")
            .ToArray();
    }
}

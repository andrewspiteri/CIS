using Cis.Abstractions;

namespace Cis.Modules.Graph;

public sealed class GraphDoctorCheck : ICisRepositoryDoctorCheck
{
    private readonly GraphValidator _validator;

    public GraphDoctorCheck(GraphValidator validator)
    {
        _validator = validator;
    }

    public string Name => "graph";

    public IReadOnlyList<CisRepositoryDoctorFinding> Inspect(CisRepositoryContext context)
    {
        var validation = _validator.Validate(context.RepositoryPath, strict: false);
        if (!validation.GraphAvailable)
        {
            return
            [
                new CisRepositoryDoctorFinding(
                    "CIS-GRAPH-DOCTOR-001",
                    "warning",
                    "context-graph",
                    "The local context graph is unavailable.",
                    validation.Diagnostics.SelectMany(diagnostic => diagnostic.Evidence).Take(20).ToArray(),
                    "Build the disposable local graph, then validate it.",
                    "cis graph build",
                    "review-required"),
            ];
        }

        return validation.Diagnostics.Select(diagnostic => new CisRepositoryDoctorFinding(
                diagnostic.Code,
                diagnostic.Severity,
                "context-graph",
                diagnostic.Message,
                diagnostic.Evidence,
                SuggestedFix(diagnostic),
                FixCommand(context, diagnostic),
                "review-required"))
            .ToArray();
    }

    private static string SuggestedFix(CisGraphDiagnostic diagnostic)
        => diagnostic.Code switch
        {
            "CIS-GRAPH-VALIDATE-IDENTITY-001" =>
                "Reconcile the repository profile so symbols and tests have confirmed component identities.",
            _ when diagnostic.Code.StartsWith("CIS-GRAPH-VALIDATE-STALE-", StringComparison.Ordinal) =>
                "Rebuild the graph from current canonical and implementation inputs.",
            _ => "Review the graph diagnostic; rebuild derived state after correcting canonical inputs.",
        };

    private static string FixCommand(CisRepositoryContext context, CisGraphDiagnostic diagnostic)
        => diagnostic.Code switch
        {
            "CIS-GRAPH-VALIDATE-IDENTITY-001" =>
                $"cis repo init --root {context.DocumentationRoot} --dry-run",
            _ when diagnostic.Code.StartsWith("CIS-GRAPH-VALIDATE-STALE-", StringComparison.Ordinal) =>
                "cis graph build",
            _ => "cis graph validate",
        };
}

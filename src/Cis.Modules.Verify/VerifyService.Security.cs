using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Plan;

namespace Cis.Modules.Verify;

public sealed partial class VerifyService
{
    private void ValidateWorkspaceSecurity(CisRepositoryContext authority, List<VerifyFinding> findings)
    {
        foreach (var repository in Repositories(authority, findings))
        {
            try
            {
                var context = _resolver.Resolve(repository.RepositoryPath).Context;
                if (context is null || context.RepositoryId != repository.Id || context.DocumentationRoot != repository.DocumentationRoot)
                {
                    findings.Add(new("error", "CIS-VERIFY-SECURITY-OWNER", "Security evidence requires a matching registered repository identity and documentation root.", repository.Id + "::"));
                    continue;
                }
                var adopted = CisEngineeringPolicy.Read(context.RepositoryPath)?.RequireTaskCompletion == true;
                if (adopted || File.Exists(Path.Combine(context.DocumentationPath, "references", "security-suite-profile.md")))
                    ValidateReconciledSecurity(context, adopted, findings);
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            {
                findings.Add(new("error", "CIS-VERIFY-SECURITY-INTEGRITY", "Cannot validate native security evidence: " + error.Message, repository.Id + "::"));
            }
        }
    }

    private void ValidateReconciledSecurity(CisRepositoryContext context, bool adopted, List<VerifyFinding> findings)
    {
        var result = _security!.Status(context.RepositoryPath, null);
        var manifest = result.Manifest;
        var path = context.RepositoryId + "::" + (manifest is null ? ".cis/local/security" : $".cis/local/security/runs/{result.RunId}/manifest.json");
        if (manifest is null)
        {
            findings.Add(new("error", "CIS-VERIFY-SECURITY-EVIDENCE", "No reconciled security run is available for the repository.", path));
            return;
        }
        foreach (var diagnostic in result.Diagnostics.Where(x => x.StartsWith("ERROR:", StringComparison.Ordinal)))
            findings.Add(new("error", "CIS-VERIFY-SECURITY-INTEGRITY", diagnostic, path));
        if (manifest.RepositoryId != context.RepositoryId || manifest.RunId != result.RunId)
            findings.Add(new("error", "CIS-VERIFY-SECURITY-OWNER", "Security manifest belongs to a different repository or run.", path));
        if (manifest.Status is not ("passed" or "passed-with-findings"))
            findings.Add(new("error", "CIS-VERIFY-SECURITY-STATUS", $"Security run '{manifest.RunId}' is {manifest.Status}.", path));
        var revision = GitRevision(context.RepositoryPath);
        if (revision != "unavailable" && !string.Equals(manifest.RepositoryRevision, revision, StringComparison.OrdinalIgnoreCase)
            || (adopted || manifest.InputDigest is not null) && manifest.InputDigest != CisExecutionIdentity.Capture(context))
            findings.Add(new("error", "CIS-VERIFY-SECURITY-STALE", "Security evidence does not match the current repository revision or execution inputs.", path));
        var rejection = manifest.SchemaVersion != 1 ? "Unsupported security manifest schema."
            : EngineeringSecurityEvidence.Rejection(context.RepositoryPath, JsonSerializer.SerializeToElement(manifest, JsonOptions), context.DocumentationRoot);
        if (rejection is not null)
            findings.Add(new("error", "CIS-VERIFY-SECURITY-INTEGRITY", rejection, path));
    }
}

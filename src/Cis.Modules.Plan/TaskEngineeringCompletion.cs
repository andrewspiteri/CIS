using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Change;

namespace Cis.Modules.Plan;

/// <summary>Assesses each task target while retaining canonical tasks and native agent records at their authority.</summary>
internal sealed class TaskEngineeringCompletion(ICisRepositoryContextResolver resolver, ICisWorkspaceRegistry? registry,
    ICisEngineeringAssessment? engineering, ICisGraphSnapshotReader? graph, IReadOnlyList<ICisRepositoryDoctorCheck> checks)
{
    internal (EngineeringCompletionReceipt? Template, IReadOnlyList<string> Errors) Prepare(ChangeDossier change, PlanWorkItem task,
        IReadOnlyList<string>? fallbackTargets, string? document, string? target)
    {
        try
        {
            var scopes = new TaskCompletionScopes(resolver, registry).Resolve(change, task, fallbackTargets);
            var scope = target is null ? scopes.Count == 1 ? scopes[0] : null : scopes.SingleOrDefault(item => item.Context.RepositoryId == target);
            if (scope is null) return (null, ["Select one declared task target with --target: " + string.Join(", ", scopes.Select(item => item.Context.RepositoryId))]);
            var context = scope.Context;
            var assessment = engineering?.Assess(context, task.Category);
            if (assessment is not { Adopted: true } || graph is null)
                return (null, ["Engineering defaults must be adopted by the selected target and the graph module available."]);
            if (assessment.Diagnostics.Count > 0) return (null, assessment.Diagnostics);
            var template = EngineeringCompletionReview.Template(change.Id, task, Policy(context), graph.ReadMetadata(context.RepositoryPath),
                assessment, CisExecutionIdentity.Capture(context), document);
            return (template with { RepositoryId = context.RepositoryId, ReceiptPath = scope.ReceiptPath }, []);
        }
        catch (Exception error) when (Recoverable(error)) { return (null, [error.Message]); }
    }

    internal IReadOnlyList<string> Review(ChangeDossier change, PlanWorkItem task, IReadOnlyList<string>? fallbackTargets,
        Func<string?> readDocument, Func<string>? readContract = null)
    {
        var errors = new List<string>();
        try
        {
            var authority = resolver.Resolve(change.RepositoryPath).Context ?? throw new InvalidDataException("Task authority context is unavailable.");
            var authorityAdopted = CisExecutionIdentity.CaptureIfAdopted(authority) is not null;
            if (!authorityAdopted && !AnyTargetAdopted(change, task, fallbackTargets)) return [];
            var scopes = new TaskCompletionScopes(resolver, registry).Resolve(change, task, fallbackTargets);
            var adoptedTargets = scopes.ToDictionary(scope => scope.Context.RepositoryId,
                scope => CisExecutionIdentity.CaptureIfAdopted(scope.Context) is not null, StringComparer.Ordinal);
            if (!authorityAdopted && adoptedTargets.Values.All(adopted => !adopted)) return [];
            var authorityIdentity = CisExecutionIdentity.Capture(authority);
            var dossierIdentity = CisTaskAuthorityContext.Capture(authority, change.Id);
            var document = readDocument();
            var identities = new List<(CisRepositoryContext Context, string Digest)>();
            if (scopes.Any(scope => scope.Context.RepositoryId != authority.RepositoryId))
            {
                if (graph?.ReadMetadata(authority.RepositoryPath) is not { ExitCode: 0, Freshness: "fresh", Build.Status: "complete" or "partial" })
                    errors.Add("Authority task context requires a fresh graph before participant completion.");
                var authorityAlignment = checks.Where(check => check.Name == "engineering-alignment").ToArray();
                if (authorityAlignment.Length == 0) errors.Add("Authority engineering alignment checker is unavailable.");
                errors.AddRange(authorityAlignment.SelectMany(check => check.Inspect(authority)).Where(finding => finding.Severity == "error")
                    .Select(finding => "authority: " + finding.Message));
            }
            foreach (var scope in scopes)
            {
                var context = scope.Context;
                try
                {
                    var adopted = adoptedTargets[context.RepositoryId];
                    if (engineering is null)
                    {
                        if (adopted || authorityAdopted) errors.Add($"{context.RepositoryId}: Engineering completion assessment provider is unavailable.");
                        continue;
                    }
                    var assessment = engineering.Assess(context, task.Category);
                    errors.AddRange(assessment.Diagnostics.Select(error => context.RepositoryId + ": " + error));
                    if (!assessment.Adopted)
                    {
                        errors.Add($"{context.RepositoryId}: Combined task completion requires reviewed engineering adoption for every declared target when any target or the authority has adopted it.");
                        continue;
                    }
                    var identity = CisExecutionIdentity.Capture(context);
                    identities.Add((context, identity));
                    var alignment = checks.Where(check => check.Name == "engineering-alignment").ToArray();
                    if (alignment.Length == 0) errors.Add(context.RepositoryId + ": Engineering alignment checker is unavailable.");
                    errors.AddRange(alignment.SelectMany(check => check.Inspect(context)).Where(finding => finding.Severity == "error")
                        .Select(finding => context.RepositoryId + ": " + finding.Message));
                    var participant = context.RepositoryId != authority.RepositoryId;
                    errors.AddRange(EngineeringCompletionReview.Review(context.RepositoryPath, Path.Combine(change.RepositoryPath, scope.ReceiptPath),
                        change.Id, task, Policy(context), graph?.ReadMetadata(context.RepositoryPath), assessment, identity, context.DocumentationRoot, document,
                        implementationRepository: participant ? authority.RepositoryPath : null,
                        reviewRepository: participant ? authority.RepositoryPath : null,
                        receiptRepository: authority.RepositoryPath, expectedRepositoryId: context.RepositoryId,
                        authorityInputDigest: participant ? authorityIdentity : null,
                        authorityDossierDigest: participant ? dossierIdentity : null).Select(error => context.RepositoryId + ": " + error));
                }
                catch (Exception error) when (Recoverable(error)) { errors.Add(context.RepositoryId + ": " + error.Message); }
            }
            foreach (var (context, digest) in identities)
                if (CisExecutionIdentity.Capture(context) != digest)
                    errors.Add(context.RepositoryId + ": Participant inputs changed while other repositories were being reviewed.");
            if (CisExecutionIdentity.Capture(authority) != authorityIdentity || CisTaskAuthorityContext.Capture(authority, change.Id) != dossierIdentity || readDocument() != document
                || readContract is not null && readContract() != EngineeringCompletionReview.TaskDigest(task, document))
                errors.Add("Authority or task inputs changed during completion review; refresh the complete evidence.");
        }
        catch (Exception error) when (Recoverable(error)) { errors.Add(error.Message); }
        return errors;
    }

    private static string Policy(CisRepositoryContext context) => Path.Combine(context.RepositoryPath, ".cis/engineering-defaults.json");
    private bool AnyTargetAdopted(ChangeDossier change, PlanWorkItem task, IReadOnlyList<string>? fallbackTargets)
    {
        if (!File.Exists(Path.Combine(change.RepositoryPath, ".cis/workspace.yml"))) return false;
        var resolution = registry?.Resolve(change.RepositoryPath);
        if (resolution?.Workspace is not { } workspace || resolution.Errors.Count > 0)
            throw new InvalidDataException("Task workspace adoption cannot be determined: " + string.Join("; ", resolution?.Errors ?? ["Workspace registry is unavailable."]));
        var ids = task.Targets is { Count: > 0 } ? task.Targets : fallbackTargets ?? [];
        foreach (var id in ids)
            if (!workspace.Repositories.Any(repository => repository.Id == id))
                throw new InvalidDataException("Task target adoption cannot be determined for unregistered repository: " + id);
        var adopted = false;
        foreach (var repository in workspace.Repositories.Where(repository => repository.IsProductOwned && (ids.Count == 0 || ids.Contains(repository.Id))))
        {
            var context = resolver.Resolve(repository.RepositoryPath).Context
                ?? throw new InvalidDataException("Task target adoption cannot be determined because its context is unavailable: " + repository.Id);
            adopted |= CisEngineeringPolicy.Read(context.RepositoryPath)?.RequireTaskCompletion == true;
        }
        return adopted;
    }
    private static bool Recoverable(Exception error) => error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException;
}

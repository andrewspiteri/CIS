using System.Security.Cryptography;
using System.Text;

namespace Cis.Abstractions;

/// <summary>Identifies a selected-suite projection of one actual workflow execution.</summary>
public sealed record TestReconcileScope(string Id, string WorkflowRunId,
    IReadOnlyList<string> SelectedSuiteIds, IReadOnlyList<string> OmittedSuiteIds, string Digest)
{
    public string Policy { get; init; } = "selected-suites-v1";

    public static TestReconcileScope Create(TestRunManifest manifest, IReadOnlyList<string> selected, IReadOnlyList<string> omitted)
    {
        var fields = new[] { "selected-suites-v1", manifest.RunId, manifest.WorkflowDigest, manifest.SuiteProfileDigest,
            manifest.InputDigest ?? "", manifest.CompletedAtUtc, string.Join(',', selected), string.Join(',', omitted) };
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', fields))));
        return new("RUN-SUITES-" + digest, manifest.RunId, selected, omitted, digest);
    }

    public bool Matches(TestRunManifest manifest)
    {
        static bool Safe(string id) => !string.IsNullOrEmpty(id) && id.Length <= 100 && char.IsAsciiLetterOrDigit(id[0])
            && id.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');
        static bool Canonical(IReadOnlyList<string>? ids) => ids is { Count: <= 100 }
            && ids.All(Safe) && ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() == ids.Count
            && ids.SequenceEqual(ids.Order(StringComparer.Ordinal), StringComparer.Ordinal);
        if (manifest.SchemaVersion != 2 || Policy != "selected-suites-v1" || WorkflowRunId != manifest.RunId || !Safe(WorkflowRunId)
            || string.IsNullOrWhiteSpace(manifest.InputDigest) || string.IsNullOrWhiteSpace(manifest.WorkflowDigest)
            || manifest.SuiteProfileDigest is not { Length: 64 } || !manifest.SuiteProfileDigest.All(char.IsAsciiHexDigit)
            || !Canonical(SelectedSuiteIds) || SelectedSuiteIds.Count == 0 || !Canonical(OmittedSuiteIds)
            || SelectedSuiteIds.Intersect(OmittedSuiteIds, StringComparer.OrdinalIgnoreCase).Any()
            || manifest.Suites is null || manifest.Suites.Any(suite => suite is null) || manifest.Suites.Count != SelectedSuiteIds.Count
            || !manifest.Suites.Select(suite => suite.SuiteId).Order(StringComparer.Ordinal).SequenceEqual(SelectedSuiteIds, StringComparer.Ordinal)
            || manifest.Artifacts is null || manifest.Artifacts.Any(artifact => artifact is null || artifact.RunId != manifest.RunId
                || artifact.SuiteId is null || !SelectedSuiteIds.Contains(artifact.SuiteId, StringComparer.Ordinal))
            || manifest.Suites.Any(suite => suite.Artifacts is null || suite.Artifacts.Any(artifact => artifact is null
                || artifact.RunId != manifest.RunId || artifact.SuiteId != suite.SuiteId)))
            return false;
        var expected = Create(manifest, SelectedSuiteIds, OmittedSuiteIds);
        return Id == expected.Id && Digest == expected.Digest;
    }
}

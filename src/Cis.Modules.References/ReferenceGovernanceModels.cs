namespace Cis.Modules.References;

public sealed record ReferenceDiagnostic(
    string Code,
    string Severity,
    string Kind,
    string Message,
    string Remediation,
    IReadOnlyList<string> Evidence);

public sealed record CanonicalReferenceEntry(
    string Kind,
    string Identity,
    string DisplayName,
    string Status,
    string RowDigest,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<string> Evidence,
    string Path,
    int Line,
    string? RepositoryId = null,
    string? CanonicalKey = null);

public sealed record ReferenceObservationState(
    string Kind,
    string Identity,
    string DisplayName,
    string SourcePath,
    int? Line,
    IReadOnlyList<string> Aliases,
    bool CanonicalDeclared,
    string? CanonicalIdentity,
    string? CanonicalKey = null);

public sealed record ReferenceFamilyState(
    string Kind,
    string Provider,
    string CanonicalPath,
    bool CanonicalAvailable,
    IReadOnlyList<CanonicalReferenceEntry> CanonicalEntries,
    IReadOnlyList<ReferenceObservationState> Observations);

public sealed record ReferenceInventoryDocument(
    int SchemaVersion,
    string RepositoryId,
    string RepositoryRevision,
    DateTimeOffset CreatedAtUtc,
    string Digest,
    IReadOnlyList<ReferenceFamilyState> Families,
    IReadOnlyList<ReferenceDiagnostic> Diagnostics);

public sealed record ReferenceDiscoveryResult(
    string Status,
    string? RepositoryPath,
    string? OutputPath,
    string? Digest,
    IReadOnlyList<ReferenceFamilyState> Families,
    IReadOnlyList<ReferenceDiagnostic> Diagnostics,
    bool Applied,
    bool RepositoryConfigurationValid)
{
    public int ExitCode => !RepositoryConfigurationValid ? 2
        : Diagnostics.Any(item => item.Severity == "error") ? 5 : 0;
}

public sealed record ReferenceInventoryResult(
    string Status,
    string? RepositoryPath,
    string? Digest,
    IReadOnlyList<ReferenceFamilyState> Families,
    IReadOnlyList<string> Errors)
{
    public int ExitCode => Errors.Count > 0 ? 4 : 0;
}

public sealed record ReferenceValidationResult(
    string Status,
    string? RepositoryPath,
    bool Strict,
    IReadOnlyList<ReferenceDiagnostic> Diagnostics,
    bool RepositoryConfigurationValid)
{
    public int Errors => Diagnostics.Count(item => item.Severity == "error");
    public int Warnings => Diagnostics.Count(item => item.Severity == "warning");
    public int ExitCode => !RepositoryConfigurationValid ? 2
        : Errors > 0 || Strict && Warnings > 0 ? 5 : 0;
}

public sealed record ReferenceDiffItem(
    string Kind,
    string Change,
    string Identity,
    string Message,
    IReadOnlyList<string> Evidence);

public sealed record ReferenceDiffResult(
    string Status,
    string? RepositoryPath,
    string Baseline,
    string? CurrentRevision,
    IReadOnlyList<ReferenceDiffItem> Changes,
    IReadOnlyList<ReferenceDiagnostic> Diagnostics,
    bool RepositoryConfigurationValid)
{
    public int ExitCode => !RepositoryConfigurationValid ? 2
        : Diagnostics.Any(item => item.Severity == "error") ? 5 : 0;
}

public sealed record ReferenceReconcileResult(
    string Status,
    string? RepositoryPath,
    IReadOnlyList<string> Kinds,
    int Added,
    IReadOnlyList<string> UpdatedPaths,
    IReadOnlyList<string> ProposedRows,
    IReadOnlyList<string> Diagnostics,
    bool Applied,
    bool ConfirmationRequired)
{
    public int ExitCode => Diagnostics.Any(item => item.StartsWith("ERROR:", StringComparison.Ordinal)) ? 5
        : ConfirmationRequired ? 3 : 0;
}

public sealed record SourceEvidenceEntry(
    string Id,
    string SourcePath,
    string Format,
    string RegisteredDigest,
    string Assessment,
    string Actor,
    string RegisteredAtUtc,
    string Rationale);

public sealed record SourceEvidenceAnchor(
    string Anchor,
    string Part,
    string? ParagraphId,
    int Ordinal,
    string HeadingPath,
    string TextDigest,
    string Text);

public sealed record SourceEvidenceDiff(
    string SourceId,
    string? PreviousDigest,
    string CurrentDigest,
    IReadOnlyList<string> AddedAnchors,
    IReadOnlyList<string> ChangedAnchors,
    IReadOnlyList<string> RemovedAnchors,
    IReadOnlyList<string> CitedAnchors,
    bool MaterialToBrd);

public sealed record SourceEvidenceState(
    string Id,
    string SourcePath,
    string RegisteredDigest,
    string? DetectedDigest,
    string Assessment,
    string Status,
    bool MaterialToBrd,
    string? ProjectionPath,
    IReadOnlyList<string> Diagnostics);

public sealed record SourceEvidenceResult(
    string Status,
    string? RepositoryPath,
    string? RegistryPath,
    IReadOnlyList<SourceEvidenceState> Sources,
    IReadOnlyList<string> Diagnostics,
    bool Applied,
    bool ConfirmationRequired = false)
{
    public int ExitCode => Diagnostics.Any(item => item.StartsWith("ERROR:", StringComparison.Ordinal)) ? 4
        : ConfirmationRequired ? 3 : 0;
}

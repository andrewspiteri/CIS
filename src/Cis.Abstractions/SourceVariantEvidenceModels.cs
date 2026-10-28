namespace Cis.Abstractions;

/// <summary>One canonical unit-only incremental snapshot containing an actual native test attempt.</summary>
public sealed record SourceVariantEvidencePhase(string RepositoryPath, string ProfilePath);

/// <summary>Read-only verification of one exact source replacement and three isolated native test attempts.</summary>
public sealed record SourceVariantEvidenceOptions(string RepositoryPath, string ReportPath, string ReportSha256,
    string SourceFile, string SubjectAssemblyPath, string MutantId, string NativeTestId, SourceVariantEvidencePhase Original,
    SourceVariantEvidencePhase Variant, SourceVariantEvidencePhase Restored);

/// <summary>Actual loaded managed body and its matching portable PDB source document, observed inside a native test.</summary>
public sealed record SourceVariantBodyObservation(int SchemaVersion, string AssemblyPath, string AssemblySha256,
    string ModuleVersionId, string PdbPath, string PdbSha256, string PdbId, string Document,
    string DocumentSha256, string CaptureHelperSha256);

public sealed record SourceVariantNativeObservation(int SchemaVersion, SourceVariantBodyObservation Subject,
    SourceVariantBodyObservation TestBody);

/// <summary>Hash-bound diagnostic evidence. This result never rewrites a native status or applies score credit.</summary>
public sealed record SourceVariantEvidenceResult(string Status, int ExitCode)
{
    public int SchemaVersion => 1;
    public string Policy => "cis-exact-source-variant-v1";
    public bool Qualified => false;
    public bool Partial => true;
    public string? OriginalReportSha256 { get; init; }
    public string? DefinitionSha256 { get; init; }
    public string? OriginalStatus { get; init; }
    public string? NativeTestId { get; init; }
    public string? NativeTestMethod { get; init; }
    public string? EnvironmentSha256 { get; init; }
    public string? TestingProducerSha256 { get; init; }
    public string? HelperProducerSha256 { get; init; }
    public IReadOnlyList<SourceVariantEvidencePhaseResult> Phases { get; init; } = [];
    public IReadOnlyList<MutationInputIdentity> Inputs { get; init; } = [];
    public IReadOnlyList<string> Diagnostics { get; init; } = [];
}

public sealed record SourceVariantEvidencePhaseResult(string Role, string SourceSha256, string ReportSha256,
    string StateSha256, string Fingerprint, string Outcome, SourceVariantBodyObservation Body, SourceVariantBodyObservation TestBody);

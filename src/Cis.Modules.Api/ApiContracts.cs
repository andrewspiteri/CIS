namespace Cis.Modules.Api;

public sealed record ApiOperation(
    string ApiId,
    string Version,
    string Method,
    string Path,
    string Module,
    string Host,
    string Exposure,
    string Consumer,
    string RequestContract,
    string ResponseContract,
    string Permission,
    string TrustedScopeSource,
    string OpenApiOperation,
    string RateLimitPolicy,
    string IdempotencyConcurrency,
    string CollectionSemantics,
    string ErrorContract,
    string CachePolicy,
    string DataAccessPath,
    string Status,
    string Evidence,
    string KnownTests,
    string Notes,
    IReadOnlyList<string> SourceLocations,
    string? OpenApiDocument,
    bool SourceDiscovered,
    bool DictionaryDeclared,
    bool OpenApiDeclared,
    bool DirectPersistenceEvidence);

public sealed record ApiGovernanceProfile(
    string RouteTemplate,
    string CompatibilityMode,
    int DeprecationWindowDays,
    string ProductionOpenApiExposure,
    string CorrelationHeader,
    string ClientHeader,
    IReadOnlyList<string> OpenApiCurrent,
    IReadOnlyList<string> OpenApiBaselines,
    IReadOnlyList<ApiSupportedVersion> SupportedVersions);

public sealed record ApiSupportedVersion(
    string Module,
    string MajorVersion,
    string Status,
    string Sunset,
    string Consumers,
    string Evidence);

public sealed record ApiDiagnostic(
    string Code,
    string Severity,
    string Rule,
    string Message,
    string Remediation,
    IReadOnlyList<string> Evidence);

public sealed record ApiDiscoveryManifest(
    int SchemaVersion,
    string RepositoryId,
    string GeneratedAt,
    IReadOnlyList<ApiInputDigest> Inputs);

public sealed record ApiInputDigest(string Path, string Sha256);

public sealed record ApiDiscoveryResult(
    string Status,
    string? RepositoryPath,
    string? OutputPath,
    int Operations,
    int SourceOperations,
    int DictionaryOperations,
    int OpenApiOperations,
    IReadOnlyList<ApiDiagnostic> Diagnostics,
    IReadOnlyList<ApiOperation> Items,
    bool Applied,
    bool RepositoryConfigurationValid)
{
    public int ExitCode => !RepositoryConfigurationValid ? 2 : Diagnostics.Any(item => item.Severity == "error") ? 5 : 0;
}

public sealed record ApiInventoryResult(
    string Status,
    string? RepositoryPath,
    IReadOnlyList<ApiOperation> Items,
    IReadOnlyList<string> Errors,
    bool RepositoryConfigurationValid,
    bool StateAvailable)
{
    public int ExitCode => !RepositoryConfigurationValid ? 2 : !StateAvailable ? 4 : Errors.Count > 0 ? 5 : 0;
}

public sealed record ApiValidationResult(
    string Status,
    string? RepositoryPath,
    int Operations,
    int Errors,
    int Warnings,
    IReadOnlyList<ApiDiagnostic> Diagnostics,
    bool RepositoryConfigurationValid,
    bool Strict)
{
    public int ExitCode => !RepositoryConfigurationValid ? 2 : Errors > 0 || Strict && Warnings > 0 ? 5 : 0;
}

public sealed record ApiCompatibilityFinding(
    string Code,
    string Severity,
    string Classification,
    string Method,
    string Path,
    string Message,
    string Remediation,
    IReadOnlyList<string> Evidence);

public sealed record ApiBaselineComparison(
    string Baseline,
    string Current,
    string Status,
    int Breaking,
    int PotentiallyBreaking,
    int NonBreaking);

public sealed record ApiDiffResult(
    string Status,
    string? RepositoryPath,
    string? Baseline,
    string? Current,
    int Breaking,
    int PotentiallyBreaking,
    int NonBreaking,
    IReadOnlyList<ApiCompatibilityFinding> Findings,
    IReadOnlyList<ApiBaselineComparison> Comparisons,
    IReadOnlyList<string> Errors,
    bool RepositoryConfigurationValid)
{
    public int ExitCode => !RepositoryConfigurationValid ? 2 : Errors.Count > 0 ? 4 : Breaking > 0 ? 5 : 0;
}

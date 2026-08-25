namespace Cis.Modules.Design;

public sealed record DesignTemplate(
    string Id,
    string Version,
    string Kind,
    string Description,
    int EstimatedSavedTokens);

public sealed record DesignArtifact(
    string ScreenId,
    string State,
    string Viewport,
    string Path,
    int Width,
    int Height,
    string Sha256,
    long Bytes);

public sealed record DesignResult(
    string Status,
    string? ChangeId,
    string? GateStatus,
    string? ApprovalStatus,
    string? RendererPath,
    IReadOnlyList<DesignTemplate> Templates,
    IReadOnlyList<DesignArtifact> Artifacts,
    IReadOnlyList<string> Diagnostics,
    bool Applied)
{
    public int ExitCode => Diagnostics.Any(item => item.StartsWith("ERROR:", StringComparison.Ordinal)) ? 2 : 0;
}

public sealed record DesignScaffoldRequest(
    string RepositoryPath,
    string ChangeId,
    string Feature,
    string Shell,
    IReadOnlyList<string> Components,
    bool Force = false);

public sealed record DesignReuseRequest(
    string RepositoryPath,
    string ChangeId,
    string SourceChangeId,
    string SourceScreenId,
    string TargetScreenId,
    string Rationale);

public sealed record DesignReviewRequest(
    string RepositoryPath,
    string ChangeId,
    string Reviewer,
    string Rationale);

internal sealed record WireframeAction(string Id, string DestinationPath, string DestinationScreen);

internal sealed record DesignReuseRecord(
    string TargetScreenId,
    string FrontendType,
    string SourceChangeId,
    string SourceScreenId,
    string State,
    string Viewport,
    string Path,
    int Width,
    int Height,
    string Sha256,
    string SourceWireframeSha256,
    string SourceWireframeContentSha256,
    string SourceRendererSha256,
    string SourceManifestSha256,
    string Rationale);

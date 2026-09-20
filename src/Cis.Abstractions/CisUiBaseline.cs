namespace Cis.Abstractions;

public sealed record CisUiBaselineEvidence(string RelativePath, int Line);
public sealed record CisUiBaselineFact(string Area, string Summary, IReadOnlyList<CisUiBaselineEvidence> Evidence);
public sealed record CisUiBaselineToken(string Name, string Value, CisUiBaselineEvidence Evidence);
public sealed record CisUiBaselineRepository(string Id, string RepositoryPath, int FilesRead,
    int CandidateFiles, bool Limited, IReadOnlyList<CisUiBaselineFact> Facts, IReadOnlyList<CisUiBaselineToken> Tokens)
{
    public IReadOnlyList<CisUiBaselineControl> Controls { get; init; } = [];
    public CisUiBaselinePreview? Preview { get; init; }
}
public sealed record CisUiBaselineControl(string Kind, string Title, IReadOnlyList<CisUiBaselineEvidence> Evidence);
public sealed record CisUiBaselinePreview(string Svg, int Width, int Height, string Description);
public sealed record CisUiBaselineResult(string Status, string WorkspacePath, string SourceHash, bool Cached,
    IReadOnlyList<CisUiBaselineRepository> Repositories, IReadOnlyDictionary<string, string> Suggestions,
    IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors)
{
    public int ExitCode => Errors.Count > 0 ? 5 : 0;
}

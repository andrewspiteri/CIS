using System.Threading;

namespace Cis.Abstractions;

public sealed record CisDefinitionPage(
    string Id,
    int Ordinal,
    string Title,
    string Status,
    bool Complete,
    bool Current,
    string? PrimaryPath,
    IReadOnlyList<string> ArtifactPaths,
    IReadOnlyList<string> Issues);

public sealed record CisDefinitionDictionary(
    string Kind,
    string Title,
    string RelativePath,
    string Status,
    bool Applicable,
    int EntryCount);

public sealed record CisDefinitionDiagram(
    string Id,
    string Title,
    string RelativePath,
    string SourceFormat,
    string Status);

public sealed record CisDefinitionPreview(
    string RelativePath,
    string SvgRelativePath,
    string Status,
    string FontFamily,
    string Density,
    string Radius,
    IReadOnlyDictionary<string, string> Colors,
    IReadOnlyList<string> Components,
    IReadOnlyList<string> Surfaces);

public sealed record CisDefinitionWizardResult(
    string Status,
    string? WorkspacePath,
    string? AuthorityRepositoryId,
    string? SessionId,
    string? CurrentPage,
    bool Active,
    bool ReadyToActivate,
    IReadOnlyList<CisDefinitionPage> Pages,
    IReadOnlyList<CisDefinitionDictionary> Dictionaries,
    IReadOnlyList<CisDefinitionDiagram> Diagrams,
    CisDefinitionPreview? Preview,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    bool Applied)
{
    public int ExitCode => Errors.Count > 0 ? 5 : 0;
}

/// <summary>
/// Allows the definition coordinator to derive downstream draft artifacts from validated,
/// current upstream drafts. The allowance is process-local and cannot weaken ordinary CIS gates.
/// </summary>
public static class CisDefinitionDraftScope
{
    private static readonly AsyncLocal<int> Depth = new();

    public static bool IsActive => Depth.Value > 0;

    public static IDisposable Enter()
    {
        Depth.Value++;
        return new Scope();
    }

    public static bool Accepts(bool valid, bool current, string? effectiveStatus)
        => valid && current && (string.Equals(effectiveStatus, "Active", StringComparison.OrdinalIgnoreCase)
            || IsActive && string.Equals(effectiveStatus, "Ready for Approval", StringComparison.OrdinalIgnoreCase));

    private sealed class Scope : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Depth.Value = Math.Max(0, Depth.Value - 1);
        }
    }
}

namespace Cis.Abstractions;

/// <summary>Derived feature proposals; never changes or approves the product architecture.</summary>
public interface ICisFeatureArchitectureGenerator
{
    CisFeatureArchitectureResult Run(CisFeatureArchitectureInput input, bool prepare, Func<bool> stillCurrent);
}

public sealed record CisFeatureArchitectureInput(string WorkspacePath, string Slug, string Title,
    string ProductName, string InputHash, string Source, string Direction, string ProductArchitecture,
    string TechnicalIntent, string ComponentSheet, IReadOnlyList<string> RepositoryIds);
public sealed record CisFeatureArchitectureDiagram(string Id, string Title, string Level, string Svg, string Notes);
public sealed record CisFeatureArchitectureResult(string Status, string? InputHash,
    IReadOnlyList<CisFeatureArchitectureDiagram> Diagrams, IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors)
{
    public string? Summary { get; init; }
    public string? Provider { get; init; }
    public string? Model { get; init; }
    public bool Cached { get; init; }
    public int ExitCode => Errors.Count == 0 ? 0 : 5;
}

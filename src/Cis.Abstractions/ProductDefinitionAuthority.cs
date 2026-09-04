namespace Cis.Abstractions;

public sealed record CisProductDefinitionAuthority(
    bool Applicable,
    bool Active,
    string? SessionId,
    string? ActivatedAtUtc,
    string? BaselineHash,
    IReadOnlyList<string> Errors);

/// <summary>
/// Resolves the exact consolidated high-level product-definition baseline that a feature
/// specification must consume. Repositories that have never used the definition wizard are
/// not made incompatible; once a wizard session exists, its activation and digest are binding.
/// </summary>
public interface ICisProductDefinitionAuthority
{
    CisProductDefinitionAuthority Evaluate(string repositoryPath);
}

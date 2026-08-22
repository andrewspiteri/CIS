namespace Cis.Abstractions;

public sealed record CisRepositoryContextResolution(
    CisRepositoryContext? Context,
    IReadOnlyList<string> Errors)
{
    public bool IsSuccess => Context is not null && Errors.Count == 0;
}

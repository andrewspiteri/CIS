namespace Cis.Abstractions;

public sealed record ChangeReadinessResult(
    string Check,
    bool Applicable,
    bool Ready,
    IReadOnlyList<string> Errors);

public interface IChangeReadinessCheck
{
    ChangeReadinessResult Evaluate(string repositoryPath);
}

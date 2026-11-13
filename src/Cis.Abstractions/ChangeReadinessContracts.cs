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

public sealed record ChangeDocumentApplicability(
    bool? VisualUi,
    bool ManualTestCatalogue,
    IReadOnlyList<string> Errors);

public sealed record ChangeDocumentApplicabilitySource(
    bool Applicable,
    bool? VisualUi,
    IReadOnlyList<string> Errors);

public interface IChangeDocumentApplicabilitySource
{
    ChangeDocumentApplicabilitySource DocumentApplicability(string repositoryPath);
}

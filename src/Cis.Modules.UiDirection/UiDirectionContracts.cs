using Cis.Modules.SolutionDesign;

namespace Cis.Modules.UiDirection;

public sealed record UiDirectionQuestion(
    string Id,
    string Area,
    string Question,
    string Why,
    IReadOnlyList<string> CommonOptions,
    string SuggestedAnswer,
    string Status,
    string? Answer,
    string? AnsweredBy,
    string? AnsweredAtUtc,
    string ResolutionSource,
    string? Confidence,
    IReadOnlyList<string> Evidence);

public sealed record UiDirectionQuestionnaireResult(
    string Status,
    string? WorkspacePath,
    string? AuthorityRepositoryId,
    string? RelativePath,
    string? SolutionDesignVersion,
    bool Current,
    bool Complete,
    int AnsweredCount,
    int UnansweredCount,
    IReadOnlyList<UiDirectionQuestion> Questions,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    bool Applied)
{
    public int ExitCode => Status == "blocked" ? 5 : Errors.Count > 0 ? 2 : Status == "missing" ? 4 : Current ? 0 : 5;
}

public sealed record UiDirectionValidation(
    bool Valid,
    bool Current,
    string EffectiveStatus,
    string DocumentStatus,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

public sealed record UiDirectionResult(
    string Status,
    string? WorkspacePath,
    string? AuthorityRepositoryId,
    string? RelativePath,
    string? SolutionDesignVersion,
    string? QuestionnaireVersion,
    UiDirectionValidation? Validation,
    IReadOnlyList<string> Errors,
    bool Applied)
{
    public int ExitCode => Status == "blocked" ? 5
        : Errors.Count > 0 ? 2
        : Status == "missing" ? 4
        : Validation is { Valid: false } ? 5
        : 0;
}

public interface IUiDirectionSolutionDesignSource
{
    SolutionDesignResult Status(string workspacePath);
}

internal sealed class UiDirectionSolutionDesignSource(SolutionDesignService service)
    : IUiDirectionSolutionDesignSource
{
    public SolutionDesignResult Status(string workspacePath) => service.Status(workspacePath);
}

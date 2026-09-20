using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cis.Abstractions;

public interface ICisUiBaselineDiscovery
{
    CisUiBaselineResult Discover(string workspacePath);
}

/// <summary>Draft feature-definition visuals, separate from delivery design approval.</summary>
public interface ICisFeatureScreenGenerator
{
    CisFeatureScreensResult Run(CisFeatureScreenInput input, bool prepare, Func<bool> stillCurrent);
}

public sealed record CisFeatureScreenInput(string WorkspacePath, string Slug, string Title,
    string InputHash, string Source, string Direction)
{
    public IReadOnlyDictionary<string, string> ExperienceAnswers { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<CisFeatureScreenReview> Reviews { get; init; } = [];
    public string? TargetScreenKey { get; init; }
}
public sealed record CisFeatureScreenReviewRequest(string ScreenId, string ScreenRevision, string PreviewHash, string Decision, string Feedback);
public sealed record CisFeatureScreenReview(string Id, string Key, string Title, string Evidence, string FrontendType,
    string Decision, string Feedback, string Actor, string SavedAt, string SourceHash, string ScreenRevision);
public sealed record CisFeatureScreenField(string Label, string Kind, string Value);
public sealed record CisFeatureScreenAction(string Label, string Destination);
public sealed record CisFeatureScreenPlan(string Id, string Title, string FrontendType, string Route,
    string Purpose, string Layout, string Evidence, IReadOnlyList<CisFeatureScreenField> Fields,
    IReadOnlyList<CisFeatureScreenAction> Actions, IReadOnlyList<string> States);
public sealed record CisFeatureScreen(CisFeatureScreenPlan Plan, string Svg, string BaselineRepository)
{
    public string Key => CisFeatureScreenIdentity.Key(Plan.Evidence, Plan.FrontendType);
    public string Revision => CisFeatureScreenIdentity.Hash(JsonSerializer.Serialize(Plan) + Svg);
    public string? AppliedReviewId { get; init; }
}
public static class CisFeatureScreenIdentity
{
    public static string Key(string evidence, string frontend) => Hash(frontend + ":" +
        Regex.Replace(evidence.Trim(), @"^\d+(?:\.\d+)*[.)]?\s+", "").ToLowerInvariant());
    public static string Hash(string text) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
public sealed record CisFeatureScreensResult(string Status, string? InputHash,
    IReadOnlyList<CisFeatureScreen> Screens, IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors)
{
    public string? Provider { get; init; }
    public string? Model { get; init; }
    public bool Cached { get; init; }
    public IReadOnlyList<CisFeatureScreenReview> Reviews { get; init; } = [];
    public int ExitCode => Errors.Count > 0 ? 5 : 0;
}

using System.Text.Json.Serialization;

namespace Cis.Abstractions;

[JsonConverter(typeof(JsonStringEnumConverter<EngineeringGateState>))]
public enum EngineeringGateState { Passed, Failed, Missing, Stale, Skipped, Inapplicable }

public sealed record EngineeringGateRequirement(string Id, string Reason, bool AllowsInapplicability);
public sealed record EngineeringGateEvidence(string Id, EngineeringGateState State, string Rationale,
    IReadOnlyList<EngineeringEvidenceArtifact> Artifacts);
public sealed record EngineeringEvidenceArtifact(string Path, string Sha256, string? Selector = null);

/// <summary>Assessment derives obligations from current source and adopted policy, never from successful commands.</summary>
public interface ICisEngineeringAssessment
{
    EngineeringAssessment Assess(CisRepositoryContext context, string taskCategory);
}

public sealed record EngineeringAssessment(int SchemaVersion, bool Adopted,
    IReadOnlyList<EngineeringGateRequirement> RequiredGates, IReadOnlyList<string> Diagnostics);

public sealed record EngineeringDefaultsPolicy(int SchemaVersion, bool RequireTaskCompletion,
    IReadOnlyList<string> AdditionalGates, double MinimumCoverageLines = 95, double MinimumMutationScore = 80);

/// <summary>Reads the current plan-owned task contract without granting lifecycle authority.</summary>
public interface ICisEngineeringTaskContractReader
{
    string ReadTaskDigest(string repositoryPath, string changeId, string taskId);
}

public sealed record EngineeringPerformanceResult(int SchemaVersion, string Status, IReadOnlyList<string> Errors)
{
    public int ExitCode => Errors.Count == 0 ? 0 : 4;
}

public sealed record CisStoryCompletionContext(string RepositoryId, string ReceiptPath, string? TemplateJson,
    IReadOnlyList<string> Errors)
{
    public string AdoptionStatus { get; init; } = "required";
}

public interface ICisStoryEngineeringCompletion
{
    IReadOnlyList<CisStoryCompletionContext> Assess(CisStoryTaskWork work, bool prepare);
}

public static class CisStoryEngineeringContract
{
    public static string Digest(CisStoryTaskWork work)
        => "sha256:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
            {
                work.Slug,
                work.StoryId,
                work.PlanHash,
                work.Definition,
                work.Task,
                work.StoryCriteria,
                work.Direction,
                work.Constraints
            })));
}

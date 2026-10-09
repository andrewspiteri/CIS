using Cis.Abstractions;

namespace Cis.Modules.Agent;

public sealed partial class AgentService
{
    private static bool IsEngineeringReview(AgentRunManifest manifest)
        => manifest.Mode == CisAgentRunModes.Review && manifest.InputDigest is not null;

    private static string EngineeringReviewPrompt(AgentTaskEnvelope envelope, bool engineeringReview)
        => BuildPrompt(envelope) + (engineeringReview ? """

            Perform an independent read-only task review. Inspect the task, final source, relevant callers,
            contracts, operational consequences and native verification evidence. Context artifacts are
            routing aids, not proof that all relevant source was included. Expand to source for missing
            callers or uncertain summaries; report material omissions as blocking findings.
            Assess correctness, cohesive responsibilities, readability, dependencies, test adequacy,
            standards adoption and all applicable completion gates. Do not change files or grant approval.
            Include a review object in the final JSON: recommendation (ready, revise or blocked), strengths
            (string array), findings (array). Each finding requires id (TASK-REV-001, sequential), severity
            (blocking, major, minor or observation), category, location, observation and recommendation.
            Use ready only when no corrective finding remains; missing or stale evidence is not a pass.
            """ : "");
}

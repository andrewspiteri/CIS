using Cis.Abstractions;

namespace Cis.Modules.Agent;

public sealed partial class AgentService
{
    // Shared runtime excerpt of cis-validate-completion's promised-workflow rule.
    // BuildPrompt supplies it to direct workers, engineering review and story stages.
    private const string TaskWorkflowChecks = """

        Apply the promised-workflow checks in .github/skills/cis-validate-completion/SKILL.md.
        Derive expected behavior and checks from the accepted outcome and concrete acceptance before
        relying on the implementation's tests. Check the user's actual operation, the existing state
        it must preserve and the next operation that must remain possible. Trace relevant callers and
        consumers; for shared state inspect writers, readers, validators and terminal/recovery paths.
        Use supported service/controller entry points when helper tests cannot establish the promise,
        and cover alternate paths with materially different semantics. Check required denial AND
        legitimate resolution and continuation of holds, amendments or recovery. Do not mock away
        the changed rule or infer representative inputs from fixture names. State exactly what the
        checks establish and leave unverified, with concise acceptance/path/check links in the existing
        review record. Keep one proportionate independent review; required corrections stay within
        it, optional advice creates no compulsory work, and correction-round limits are ceilings.
        For a required defect escaping a reviewed handoff, use the existing correction/verification
        record to explain the missed ordinary scenario, why the old check missed it and what now
        covers the cause; inspect adjacent consumers and continuation. Internal review fixes are
        normal corrections, and ReadyForReview is distinct from formal acceptance or release.

        """;

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
            """ + TaskReviewInstructions() : "");

    private static string TaskReviewInstructions() => """

        Include a review object in the final JSON: recommendation (ready, revise or blocked), strengths
        (string array), findings (array), and necessityAssessment. Each finding requires id
        (TASK-REV-001, sequential), severity (blocking, major, minor or observation), disposition
        (required or optional), category, location, observation, recommendation, and basisReferences
        (a nonempty string array naming the accepted task/criterion, applicable adopted policy,
        authorized decision or concrete necessary invariant, or explaining why advice is only optional).
        Disposition is independent of severity. A minor unmet obligation is required; preferences and
        future improvements are optional. Do not use optional to waive an acceptance criterion or gate.
        Recommend ready only with no unresolved required finding. Recommend revise only for a required
        correctable defect. Recommend blocked only for a concrete required decision, missing authorized
        evidence or permission. Ready with required findings and revise/blocked with only optional advice
        are inconsistent evidence. Optional advice must not create compulsory corrections or questions.
        A finding may include requiresDecision (boolean). Set it true only for a concrete existing human
        decision or question that must be resolved, with the decision identified in its basis and text.
        A blocked verdict, missing evidence, provider permission failure or ordinary correctable defect
        alone does not create such a question. True requires required disposition and blocked verdict;
        omit it or use false for other findings. This flag cannot grant approval or waive a reserved gate.
        Missing or stale required evidence remains a hold. Checked CIS task completion is separate
        from review; outcome-authorized completion needs no extra human decision. Preserve actual
        reserved stakeholder and final-acceptance decisions.

        Examine the actual changed source and meaningful callers before judging necessity; organization,
        field presence, file counts and passing tests do not establish a sound or necessary design.
        In necessityAssessment explicitly conclude whether the actual diff materially adds product
        responsibilities, architecture or verification machinery (for example a lifecycle, provider
        hierarchy, registry, persistence, discovery, concurrency coordinator, extension point, runner,
        comparator, receiver, classifier, fixture mechanism or acceptance predicate).
        For ordinary direct changes or helper extraction with no such expansion, return applicable:false
        and one concise decision explaining that conclusion. No elaborate necessity document is needed.
        For material expansion, return applicable:true and the existing reasoning fields:
        invariant (the concrete necessary defect/obligation), nativeAlternative (what adopted native
        tooling or direct existing implementation already proves and the smallest capable alternative),
        costEvidence (existing measured runtime/verification cost or explicitly labelled estimate/unknown), maintenanceAndPassSemantics (maintenance
        burden and any change to pass meaning), duplicationAvoided (which duplicate/repeated work is
        removed or avoided), and decision (why to continue, simplify or remove this approach).
        An unsupported responsibility or unjustified mechanism contrary to the accepted boundary and
        adopted code-quality policy is a required correctable finding even when tests pass. A genuine
        necessary security/concurrency invariant remains required; hypothetical improvements and style
        preferences remain optional. General standards do not authorize inventing a capability.
        Use the existing diff and cost/evidence account. Do not add benchmarks, instrumentation,
        provider calls or a separate review ceremony just to populate the assessment. Ordinary helpers
        and test assertions need only the concise not-applicable conclusion. Assess only task-relevant standards and gates;
        do not manufacture an unrelated whole-repository tooling or compliance campaign.
        """;
}

namespace Cis.Modules.Repository;

internal static class EngineeringDefaultStandards
{
    internal static readonly IReadOnlyList<DefaultStandardDefinition> All =
    [
        new("code-quality", "Code Quality and Architecture Standard",
            "Keep implementation understandable, replaceable and checked with native tooling without imposing one architecture.",
            ["backend", "frontend", "architecture", "testing"], [], ["docs/specs/code-quality-architecture-plan.md"], HasCode,
            [
                new("QUALITY-001", "Changed code MUST have cohesive responsibilities, meaningful names and navigable ownership; file splitting alone MUST NOT establish readability.", "Review complete types including partial declarations and representative callers."),
                new("QUALITY-002", "Dependencies SHOULD be explicit and assembled at a composition boundary where replacement is needed; a DI container or interface for every class MUST NOT be required solely for conformity.", "Review dependency ownership, replacement points and justified existing patterns."),
                new("QUALITY-003", "Applicable native analyzers, lint and formatting checks MUST execute with declared scope and enforced severity; installation alone MUST NOT count as a passing gate.", "Run real checks and demonstrate a seeded violation; preserve stronger adopted policy."),
                new("QUALITY-004", "Architecture tests MUST enforce an owned boundary and detect a violating fixture; zero matched targets MUST NOT pass silently.", "Inspect native architecture tests, selected scope and negative results."),
                new("QUALITY-005", "Independent logic review MUST use a separate identified review run, source-backed context and a selectable provider; unresolved blocking findings MUST prevent completion.", "Inspect provider-neutral run/result evidence, relevant callers and finding dispositions."),
            ], SourceRepository: "ChangeImpactStudio"),
        new("agent-interface", "Application CLI and Skills Standard",
            "Make supported application workflows usable by agents through ordinary trusted application boundaries.",
            ["backend", "frontend", "tooling"], [], ["docs/specs/agent-interface-defaults-plan.md"], HasCode,
            [
                new("INTERFACE-001", "Applications SHOULD expose supported use cases through a native CLI with discoverable noninteractive commands, stable exit codes and versioned structured output; inapplicability MUST have a concrete rationale.", "Execute a representative workflow and denied/error cases without interactive prompting."),
                new("INTERFACE-002", "CLI adapters MUST reuse application authorization and behavior boundaries and MUST NOT create privileged storage shortcuts.", "Compare CLI and other adapter outcomes and inspect trusted boundary calls."),
                new("INTERFACE-003", "Portable skills SHOULD cover discovery, representative use, verification and diagnosis, with actual prerequisites, commands, effects and failure handling.", "Validate command/schema drift and reproduce the workflow from the canonical skill."),
            ], SourceRepository: "ChangeImpactStudio"),
        new("iteration-completion", "Iteration Alignment and Completion Standard",
            "Refresh standards and evidence as the project grows, then close only verified current task scope.",
            ["repository-governance", "verification"], [], ["docs/specs/iteration-alignment-plan.md"], _ => true,
            [
                new("ITERATION-001", "Every completed task MUST have current graph, CIS dependency, all-standard and skill alignment assessment that preserves human-owned customizations and adopted alternatives.", "Inspect reconciliation preview, final graph identity and task closing evidence."),
                new("ITERATION-002", "The required gate inventory MUST be derived independently of configured successful commands; every applicable required gate MUST pass before completion.", "Deliberately omit a required gate and verify that completion fails."),
                new("ITERATION-003", "Failed, missing, stale, skipped and justified inapplicable states MUST remain distinct; inapplicability MUST NOT be counted as a pass.", "Exercise each gate state with scoped evidence and verify transition behavior."),
                new("ITERATION-004", "Completion MUST connect current scoped requirements, tasks, source, verification and review dispositions; later phases MUST remain visible without granting operational activation.", "Review exact scoped requirement IDs and unresolved decisions/findings."),
                new("ITERATION-005", "Implementation and review MUST inspect authoritative source when graph or summary context is incomplete or inaccurate, and MUST refresh affected context after intervening edits.", "Exercise missing-caller, misleading-summary and changed-input cases."),
                new("ITERATION-006", "Local evidence hashes MUST NOT be treated as authenticated attestations. The assigned reviewer MUST inspect native execution provenance, implementation authorship and material evidence omissions.", "Verify the retained implementation result and separate reviewer session; reject hand-written passing manifests as execution proof."),
            ], SourceRepository: "ChangeImpactStudio"),
    ];

    private static bool HasCode(RepositoryClassification classification)
        => classification.DeclaredStack == "csharp" || classification.Components.Any(component => component.Languages.Any(language => language is "csharp" or "typescript" or "javascript" or "python" or "swift" or "kotlin" or "gdscript"));
}

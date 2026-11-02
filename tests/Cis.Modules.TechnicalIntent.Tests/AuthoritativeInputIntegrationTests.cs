using System.Text.Json;
using Cis.Modules.Brd;
using Xunit;

namespace Cis.Modules.TechnicalIntent.Tests;

public sealed partial class TechnicalIntentWorkflowTests
{
    [Fact]
    public void AcceptedPair_ImportsAlternativeRequirementsAndReusesTechnicalFactsThroughNormalConsumers()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true, includeImplementation: false, completeQuestionnaire: false);
        const string business = """
            # Synthetic observation product
            ## Overview
            Operators inspect original observation evidence.
            ## Business objectives
            Retain truthful evidence and report gaps.
            ## Product scope
            Local observation capture is included; browser UI is excluded in phase 1 and deferred to phase 2.
            ## Actors and roles
            The product operator owns local capture requests.
            ## Business capabilities
            Capture and inspect original evidence.
            ## Functional requirements
            ## Product requirements
            ### REQ-01 Evidence retention
            The operator must retain observations for 30 days unless a legal hold applies.
            - Never fabricate missing observations.
            - Preserve the original units and source identity.
            ## Quality requirements
            NFR-01: The system must report capture gaps within 30 seconds.
            ## Assumptions and constraints
            Use the trusted-local boundary. No application login is required.
            ## Success criteria
            Operators can replay the original evidence and its exceptions.
            ## Requirements traceability
            REQ-01 is supplied by the accepted product source.
            ## Open questions
            None.
            """;
        var businessPath = Path.Combine(environment.Authority.Path, "docs/specs/business-requirements.md");
        File.WriteAllText(businessPath, business);
        SelectTechnicalDirection(environment, SuppliedTechnicalDirection);
        File.WriteAllText(Path.Combine(environment.Authority.Path, ".cis/product-documents.json"),
            JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["business"] = "docs/specs/business-requirements.md",
                ["technical"] = "docs/imports/technical/source.md",
            }));
        var imported = environment.Brd.Reconcile(environment.Authority.Path);
        Assert.True(imported.Validation!.Valid, string.Join("\n", imported.Validation.Errors));
        Assert.Equal(0, environment.Brd.Approve(environment.Authority.Path, "Product owner", "Adopt the supplied source baseline.").ExitCode);
        environment.RebuildAuthorityGraph();
        AdoptTechnicalDirection(environment);

        var questions = environment.Questionnaire.Initialize(environment.Authority.Path);
        Assert.True(questions.Complete, string.Join("\n", questions.Errors.Concat(questions.Warnings)));
        Assert.DoesNotContain(questions.Questions, item => item.Status == "Unanswered");
        Assert.Empty(environment.Brd.Questions(environment.Authority.Path).Questions);
        environment.Authority.Write("docs/design/ui-direction.md", "---\ncis:\n  visual_ui: false\n---\nNo visual interface.\n");
        var backlog = environment.CreateBacklog();
        var built = backlog.Build(environment.Authority.Path);
        Assert.Equal(0, built.ExitCode);
        Assert.Equal("REQ-01", Assert.Single(built.Items).RequirementId);
        Assert.Equal(0, backlog.Approve(environment.Authority.Path, "Product owner", "Accept the retained requirement.").ExitCode);
        var feature = backlog.StartFeature(environment.Authority.Path, "HLT-REQ-01");
        Assert.Equal(0, feature.ExitCode);
        var delivered = File.ReadAllText(Path.Combine(environment.Authority.Path, feature.RelativePath!));
        Assert.Contains("30 days unless a legal hold applies", delivered);
        Assert.Contains("Never fabricate missing observations", delivered);
        Assert.Contains("Preserve the original units and source identity", delivered);
        Assert.Equal("Active", environment.Brd.Reconcile(environment.Authority.Path).Validation!.EffectiveStatus);
        Assert.True(environment.Questionnaire.Status(environment.Authority.Path).Complete);
        Assert.Equal("unchanged", environment.Questionnaire.Initialize(environment.Authority.Path).Status);
    }
}

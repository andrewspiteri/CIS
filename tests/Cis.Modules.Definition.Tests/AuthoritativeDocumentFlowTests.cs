using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;
using Cis.Host;
using Xunit;

namespace Cis.Modules.Definition.Tests;

public sealed partial class DefinitionWizardTests
{
    private const string AuthoritativeBusinessSource = """
            # Observation product BRD
            ## Overview
            Preserve original operator observations.
            ## Business objectives
            Operators inspect truthful evidence.
            ## Product scope
            Local capture is included. Browser UI is deferred to phase 2.
            ## Actors and roles
            The operator owns capture requests.
            ## Business capabilities
            Capture and inspect observations.
            ## Product requirements
            REQ-01: The operator must retain observations for 30 days unless a legal hold applies.
            ## Quality requirements
            No fabricated observations are permitted.
            ## Assumptions and constraints
            Preserve source identity and original units.
            ## Success criteria
            The operator can replay the original evidence.
            ## Requirements traceability
            REQ-01 is supplied by this product source.
            ## Open questions
            None.
            """;
    private const string AuthoritativeTechnicalSource = """
            # Observation product Technical Intent
            #### Business baseline
            Preserve the BRD scope, conditions and exclusions.
            #### Design principles
            Use deterministic policy and explicit ownership.
            #### Technology and ownership
            | Subject | Direction |
            | --- | --- |
            | Product surfaces | Use a local CLI only. |
            | Frontend | No frontend or browser UI is required in this phase. |
            | Backend | Use C# and .NET with ASP.NET Core. |
            | Architecture | Use a modular monolith with explicit module boundaries. |
            | Ownership | Use one repository and one product-owned release package. |
            | Persistence | The primary data store is PostgreSQL. |
            | Contracts | Use versioned HTTP/JSON APIs; preserve old clients for 90 days. |
            | Access | No application authentication is required; preserve the trusted-local identity boundary. |
            | Hosting | Run on Windows hosts as supervised Windows Services. |
            | Operations | Use structured privacy-safe logs, metrics, readiness and recovery runbooks. |
            | Security | Secrets must not enter logs; retain sensitive configuration locally. |
            | Assurance | Use unit, integration, acceptance and recovery tests. |
            | Constraints | The timeout defaults to 45 seconds, except health checks use 2 seconds. Browser UI is deferred to phase 2. |
            #### Runtime architecture
            Preserve the trusted-local process boundary and modular monolith.
            #### Business invariants
            Writes must commit atomically, preserving business conditions.
            #### Service contract
            Use versioned HTTP/JSON APIs and compatibility tests.
            #### Trust and security
            Use the trusted-local boundary; do not send secrets to telemetry.
            #### Shutdown, upgrades and recovery
            Retain structured logs and verify recovery before changing stored state.
            #### Acceptance and verification
            Use native acceptance and recovery tests.
            #### Technical decisions and constraints
            No cloud services are required. Preserve the timeout default and its exception.
            #### Decision closure
            No open technical decisions remain. All technical choices are closed for the current phase.
            """;

    [Theory]
    [InlineData(null)]
    [InlineData("unreadable-business")]
    [InlineData("conflicting-technical")]
    public void SelectedPair_PreparesTechnicalPageWithoutTranscriptionAndKeepsFinalAdoptionPending(string? importProblem)
    {
        using var repository = TemporaryRepository.Create();
        using var sources = TemporaryRepository.Create();
        using var application = CreateApplication();
        Assert.Equal(0, Invoke(application, ["workspace", "init", "--repo", repository.Path, "--root", "docs/cis",
            "--ecosystem", "sample", "--product", "sample", "--yes", "--format", "json"]).ExitCode);
        const string business = AuthoritativeBusinessSource;
        const string technical = AuthoritativeTechnicalSource;
        var suppliedBusiness = importProblem == "unreadable-business" ? business + "\n<!-- Unclosed source comment" : business;
        var suppliedTechnical = importProblem == "conflicting-technical" ? technical + "\n#### Current persistence\nThe primary data store is SQLite.\n" : technical;
        foreach (var (role, name, content) in new[] { ("business", "BRD.md", suppliedBusiness), ("technical", "Technical-Intent.md", suppliedTechnical) })
        {
            var path = Path.Combine(sources.Path, name);
            File.WriteAllText(path, content);
            var loaded = Invoke(application, ["definition", "documents", "--workspace", repository.Path, "--role", role,
                "--source", path, "--format", "json"]);
            Assert.Equal(0, loaded.ExitCode);
        }
        Assert.Equal(0, Invoke(application, ["graph", "build", "--workspace", repository.Path, "--format", "json"]).ExitCode);

        var prepared = Invoke(application, ["definition", "prepare", "--workspace", repository.Path, "--page", "technical", "--format", "json"]);

        var result = JsonSerializer.Deserialize<CisDefinitionWizardResult>(prepared.Output, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        if (importProblem is not null)
        {
            Assert.NotEqual(0, prepared.ExitCode);
            Assert.Contains(result.Errors, error => error.Contains("import problem", StringComparison.OrdinalIgnoreCase)
                || error.Contains("input conflict", StringComparison.OrdinalIgnoreCase));
            Assert.Empty(result.TechnicalQuestions!.Questions);
            Assert.Equal(suppliedBusiness, File.ReadAllText(Path.Combine(sources.Path, "BRD.md")));
            Assert.Equal(suppliedTechnical, File.ReadAllText(Path.Combine(sources.Path, "Technical-Intent.md")));
            return;
        }
        Assert.True(prepared.ExitCode == 0, string.Join("\n", result.Errors));
        Assert.True(result.TechnicalQuestions!.Complete, prepared.Output);
        Assert.True(result.Pages.Single(page => page.Id == "technical").Complete, prepared.Output);
        Assert.All(result.TechnicalQuestions.Questions.Where(question => question.Status == "Derived"), question =>
        {
            Assert.Equal("selected-document", question.ResolutionSource);
            Assert.Null(question.AnsweredBy); Assert.Null(question.AnsweredAtUtc);
            Assert.NotEmpty(question.Evidence);
        });
        Assert.Equal(3, result.TechnicalQuestions.Questions.Count(question => question.Status == "Not requested"));
        var selections = CisProductDocumentPaths.Read(repository.Path);
        foreach (var role in new[] { "business", "technical" })
        {
            var text = File.ReadAllText(Path.Combine(repository.Path, selections[role]));
            Assert.DoesNotContain("status: Active", text);
            Assert.Contains("approved_by: null", text);
        }
        Assert.False(result.ReadyToActivate); // Later product-definition pages still need their existing review.
        Assert.Equal(business, File.ReadAllText(Path.Combine(sources.Path, "BRD.md")));
        Assert.Equal(technical, File.ReadAllText(Path.Combine(sources.Path, "Technical-Intent.md")));
        var reopened = Invoke(application, ["definition", "prepare", "--workspace", repository.Path, "--page", "technical", "--format", "json"]);
        Assert.True(reopened.ExitCode == 0, reopened.Output + reopened.Error);
        var repeat = JsonSerializer.Deserialize<CisDefinitionWizardResult>(reopened.Output, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.True(repeat.TechnicalQuestions!.Complete);
        Assert.Equal(result.TechnicalQuestions.Questions.Select(question => question.Id), repeat.TechnicalQuestions.Questions.Select(question => question.Id));
    }

    [Fact]
    public void AcceptedTechnicalInput_StatusRetainsArchitectureAfterCompatibleBrdChange()
    {
        using var repository = TemporaryRepository.Create();
        using var sources = TemporaryRepository.Create();
        using var application = CreateApplication();
        Assert.Equal(0, Invoke(application, ["workspace", "init", "--repo", repository.Path, "--root", "docs/cis",
            "--ecosystem", "sample", "--product", "sample", "--yes", "--format", "json"]).ExitCode);
        JsonElement Run(CisApplication app, params string[] args)
        {
            var result = Invoke(app, [.. args, "--workspace", repository.Path, "--format", "json"]);
            Assert.True(result.ExitCode == 0, result.Output + result.Error);
            using var document = JsonDocument.Parse(result.Output);
            return document.RootElement.Clone();
        }
        Run(application, "graph", "build");
        Run(application, "brd", "init", "--title", "Supplied direction fixture");
        var brdPath = Path.Combine(repository.Path, "docs/cis/specs/business-requirements.md");
        var brd = Regex.Replace(File.ReadAllText(brdPath), "(?m)^TODO: Complete .+$", "Stakeholders recorded complete and testable requirements.");
        brd = Regex.Replace(brd, "(?ms)^## Functional requirements\\s*$.*?(?=^## )",
            "## Functional requirements\n\n| ID | Requirement | Priority | Acceptance intent |\n| --- | --- | --- | --- |\n| BRD-FR-001 | A user shall view the product. | Must | The product view is available. |\n\n");
        File.WriteAllText(brdPath, brd + "\n## Current-phase exclusions\n\nAI and learned models are excluded from this phase.\n");
        Run(application, "graph", "build");
        // These approvals belong only to this synthetic temporary product.
        Run(application, "brd", "approve", "--reviewer", "Fixture product owner", "--reason", "Accept the current-phase requirements and exclusions.");
        var technicalSource = AuthoritativeTechnicalSource.Replace("#### Runtime architecture",
            "| Models | AI and learned models are explicitly excluded from this phase. |\n#### Runtime architecture", StringComparison.Ordinal);
        var sourcePath = Path.Combine(sources.Path, "Supplied Technical Intent.md");
        File.WriteAllText(sourcePath, technicalSource);
        var sourceBytes = File.ReadAllBytes(sourcePath);
        Run(application, "definition", "documents", "--role", "technical", "--source", sourcePath);
        Run(application, "graph", "build");
        Run(application, "technical-intent", "init");
        var adopted = Run(application, "technical-intent", "approve", "--reviewer", "Fixture technical owner", "--reason", "Accept the supplied current-phase technical baseline.");
        Assert.Equal("Active", adopted.GetProperty("validation").GetProperty("effectiveStatus").GetString());
        var established = Run(application, "technical-intent", "questions", "init");
        var accepted = established.GetProperty("questions").EnumerateArray().Single(question => question.GetProperty("id").GetString() == "TI-Q-004").Clone();
        Assert.Equal("Derived", accepted.GetProperty("status").GetString());
        Assert.Equal("accepted-document", accepted.GetProperty("resolutionSource").GetString());
        Assert.Contains("modular monolith", accepted.GetProperty("answer").GetString(), StringComparison.OrdinalIgnoreCase);
        var technicalPath = Path.Combine(repository.Path, CisProductDocumentPaths.Read(repository.Path)["technical"]);
        var approvedTechnicalBytes = File.ReadAllBytes(technicalPath);

        File.AppendAllText(brdPath, "\n## AI governance\n\nBRD-FR-016: AI remains excluded from the current phase; any future AI proposal requires separate approval.\n");
        Run(application, "brd", "approve", "--reviewer", "Fixture product owner", "--reason", "Accept the compatible future-AI approval requirement.");
        var questionsPath = Path.Combine(repository.Path, "docs/cis/specs/technical-intent-questionnaire.md");
        var sessionPath = Path.Combine(repository.Path, ".cis/local/definition-wizard/session.json");
        var before = new[] { brdPath, technicalPath, questionsPath, sessionPath }
            .ToDictionary(path => path, path => File.Exists(path) ? File.ReadAllBytes(path) : null);
        using var restarted = CreateApplication();
        var status = Run(restarted, "definition", "status");
        var retained = status.GetProperty("technicalQuestions").GetProperty("questions").EnumerateArray()
            .Single(question => question.GetProperty("id").GetString() == "TI-Q-004");
        Assert.Equal("Derived", retained.GetProperty("status").GetString());
        Assert.Equal("accepted-document", retained.GetProperty("resolutionSource").GetString());
        Assert.Equal(accepted.GetProperty("answer").GetString(), retained.GetProperty("answer").GetString());
        Assert.Equal(accepted.GetProperty("evidence").EnumerateArray().Select(item => item.GetString()),
            retained.GetProperty("evidence").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(JsonValueKind.Null, retained.GetProperty("answeredBy").ValueKind);
        Assert.Equal(JsonValueKind.Null, retained.GetProperty("answeredAtUtc").ValueKind);
        var aggregate = Run(restarted, "technical-intent", "status").GetProperty("validation");
        Assert.False(aggregate.GetProperty("current").GetBoolean());
        Assert.NotEqual("Active", aggregate.GetProperty("effectiveStatus").GetString());
        Assert.False(status.GetProperty("readyToActivate").GetBoolean());
        Run(restarted, "definition", "status");
        foreach (var snapshot in before)
        {
            Assert.Equal(snapshot.Value is not null, File.Exists(snapshot.Key));
            if (snapshot.Value is not null) Assert.Equal(snapshot.Value, File.ReadAllBytes(snapshot.Key));
        }
        Assert.Equal(approvedTechnicalBytes, File.ReadAllBytes(technicalPath));
        Assert.Equal(sourceBytes, File.ReadAllBytes(sourcePath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AcceptedTechnicalInput_AdoptedReplacementSupersedesPreviousWorkingCopy(bool changedMeaning)
    {
        using var repository = TemporaryRepository.Create();
        using var sources = TemporaryRepository.Create();
        using var application = CreateApplication();
        Assert.Equal(0, Invoke(application, ["workspace", "init", "--repo", repository.Path, "--root", "docs/cis",
            "--ecosystem", "sample", "--product", "sample", "--yes", "--format", "json"]).ExitCode);
        JsonElement Run(CisApplication app, params string[] args)
        {
            var result = Invoke(app, [.. args, "--workspace", repository.Path, "--format", "json"]);
            Assert.True(result.ExitCode == 0, result.Output + result.Error);
            using var document = JsonDocument.Parse(result.Output);
            return document.RootElement.Clone();
        }
        JsonElement Questions(CisApplication app, bool requireCurrent)
        {
            var result = Invoke(app, ["technical-intent", "questions", "status", "--workspace", repository.Path, "--format", "json"]);
            using var document = JsonDocument.Parse(result.Output);
            if (requireCurrent)
            {
                Assert.True(result.ExitCode == 0, result.Output + result.Error);
                Assert.Empty(document.RootElement.GetProperty("errors").EnumerateArray());
            }
            else
            {
                Assert.Contains(result.ExitCode, new[] { 0, 2, 5 });
                Assert.All(document.RootElement.GetProperty("errors").EnumerateArray(),
                    error => Assert.Contains("conflict", error.GetString(), StringComparison.OrdinalIgnoreCase));
            }
            return document.RootElement.Clone();
        }
        static JsonElement Store(JsonElement questionnaire) => questionnaire.GetProperty("questions").EnumerateArray()
            .Single(question => question.GetProperty("id").GetString() == "TI-Q-006").Clone();

        Run(application, "graph", "build");
        Run(application, "brd", "init", "--title", "Source replacement fixture");
        var brdPath = Path.Combine(repository.Path, "docs/cis/specs/business-requirements.md");
        var brd = Regex.Replace(File.ReadAllText(brdPath), "(?m)^TODO: Complete .+$", "Stakeholders recorded complete and testable requirements.");
        brd = Regex.Replace(brd, "(?ms)^## Functional requirements\\s*$.*?(?=^## )",
            "## Functional requirements\n\n| ID | Requirement | Priority | Acceptance intent |\n| --- | --- | --- | --- |\n| BRD-FR-001 | A user shall view the product. | Must | The product view is available. |\n\n");
        File.WriteAllText(brdPath, brd + "\n## Current-phase exclusions\n\nAI and learned models are excluded from this phase.\n");
        Run(application, "graph", "build");
        Run(application, "brd", "approve", "--reviewer", "Fixture product owner", "--reason", "Accept requirements that permit either supported primary store.");
        var technicalA = AuthoritativeTechnicalSource.Replace("#### Runtime architecture",
            "| Models | AI and learned models are explicitly excluded from this phase. |\n#### Runtime architecture", StringComparison.Ordinal);
        var sourceA = Path.Combine(sources.Path, "Supplied Technical Intent.md");
        File.WriteAllText(sourceA, technicalA); var originalA = File.ReadAllBytes(sourceA);
        Run(application, "definition", "documents", "--role", "technical", "--source", sourceA);
        var relativeA = CisProductDocumentPaths.Read(repository.Path)["technical"];
        var workingA = Path.Combine(repository.Path, relativeA);
        Run(application, "graph", "build");
        Run(application, "technical-intent", "init");
        Run(application, "technical-intent", "approve", "--reviewer", "Fixture technical owner", "--reason", "Adopt the supplied first technical baseline.");
        Run(application, "technical-intent", "questions", "init");
        var acceptedA = Store(Questions(application, true));
        Assert.Equal("accepted-document", acceptedA.GetProperty("resolutionSource").GetString());
        Assert.Contains("PostgreSQL", acceptedA.GetProperty("answer").GetString(), StringComparison.Ordinal);
        Assert.Contains(acceptedA.GetProperty("evidence").EnumerateArray(), evidence => evidence.GetString()!.StartsWith(relativeA + ":", StringComparison.Ordinal));
        var adoptedA = File.ReadAllBytes(workingA);

        var sourceB = changedMeaning ? Path.Combine(sources.Path, "Replacement Technical Intent.md") : sourceA;
        if (changedMeaning) File.WriteAllText(sourceB, technicalA.Replace("primary data store is PostgreSQL", "primary data store is SQLite", StringComparison.Ordinal));
        if (changedMeaning) Assert.NotEqual(technicalA, File.ReadAllText(sourceB));
        var originalB = File.ReadAllBytes(sourceB);
        Run(application, "definition", "documents", "--role", "technical", "--source", sourceB);
        var relativeB = CisProductDocumentPaths.Read(repository.Path)["technical"];
        var workingB = Path.Combine(repository.Path, relativeB);
        Assert.NotEqual(relativeA, relativeB);
        Assert.Equal(originalB, File.ReadAllBytes(workingB));
        var beforeAdoption = Store(Questions(application, false));
        Assert.Equal(acceptedA.GetProperty("answer").GetString(), beforeAdoption.GetProperty("answer").GetString());
        Assert.Equal(acceptedA.GetProperty("evidence").GetRawText(), beforeAdoption.GetProperty("evidence").GetRawText());
        Assert.Contains(beforeAdoption.GetProperty("sourceFactAuthority").GetProperty("basis").EnumerateObject(), entry => entry.Name == relativeA);
        Assert.DoesNotContain(beforeAdoption.GetProperty("sourceFactAuthority").GetProperty("basis").EnumerateObject(), entry => entry.Name == relativeB);
        Assert.Equal(originalB, File.ReadAllBytes(workingB));
        Assert.Equal(adoptedA, File.ReadAllBytes(workingA));

        // This fixture explicitly authorizes B's adoption; document selection alone did not.
        Run(application, "technical-intent", "init");
        var adoptedB = Run(application, "technical-intent", "approve", "--reviewer", "Fixture technical owner", "--reason", "Adopt the explicitly selected replacement baseline.");
        Assert.Equal(relativeB, adoptedB.GetProperty("canonicalPath").GetString());
        Assert.Equal("Active", adoptedB.GetProperty("validation").GetProperty("effectiveStatus").GetString());
        var directlyAdopted = Store(Questions(application, true));
        var refreshed = Store(Run(application, "technical-intent", "questions", "init"));
        var expectedStore = changedMeaning ? "SQLite" : "PostgreSQL";
        foreach (var effective in new[] { directlyAdopted, refreshed })
        {
            Assert.Equal("Derived", effective.GetProperty("status").GetString());
            Assert.Equal("accepted-document", effective.GetProperty("resolutionSource").GetString());
            Assert.Equal("Current", effective.GetProperty("assessmentStatus").GetString());
            Assert.Contains(expectedStore, effective.GetProperty("answer").GetString(), StringComparison.Ordinal);
            if (changedMeaning) Assert.DoesNotContain("PostgreSQL", effective.GetProperty("answer").GetString(), StringComparison.Ordinal);
            Assert.Contains(effective.GetProperty("evidence").EnumerateArray(), evidence => evidence.GetString()!.StartsWith(relativeB + ":", StringComparison.Ordinal));
            Assert.DoesNotContain(effective.GetProperty("evidence").EnumerateArray(), evidence => evidence.GetString()!.StartsWith(relativeA + ":", StringComparison.Ordinal));
            var authority = effective.GetProperty("sourceFactAuthority");
            Assert.Equal("Current", authority.GetProperty("assessmentStatus").GetString());
            Assert.Contains(authority.GetProperty("basis").EnumerateObject(), entry => entry.Name == relativeB);
            Assert.DoesNotContain(authority.GetProperty("basis").EnumerateObject(), entry => entry.Name == relativeA);
            Assert.Equal(JsonValueKind.Null, effective.GetProperty("answeredBy").ValueKind);
            Assert.Equal(JsonValueKind.Null, effective.GetProperty("answeredAtUtc").ValueKind);
        }

        using var restarted = CreateApplication();
        Run(restarted, "technical-intent", "init");
        Run(restarted, "technical-intent", "questions", "init");
        var questionsPath = Path.Combine(repository.Path, "docs/cis/specs/technical-intent-questionnaire.md");
        var snapshots = new[] { workingA, workingB, sourceA, sourceB, brdPath, questionsPath,
            Path.Combine(repository.Path, CisProductDocumentPaths.SelectionFile), Path.Combine(repository.Path, ".cis/local/definition-wizard/session.json") }
            .Distinct(StringComparer.Ordinal).ToDictionary(path => path, path => File.Exists(path) ? File.ReadAllBytes(path) : null);
        var resumed = Store(Questions(restarted, true));
        Assert.Equal(refreshed.GetProperty("answer").GetString(), resumed.GetProperty("answer").GetString());
        Assert.Equal(refreshed.GetProperty("sourceFactAuthority").GetRawText(), resumed.GetProperty("sourceFactAuthority").GetRawText());
        var definition = Run(restarted, "definition", "status");
        var projected = Store(definition.GetProperty("technicalQuestions"));
        Assert.Equal("accepted-document", projected.GetProperty("resolutionSource").GetString());
        Assert.Equal("Current", projected.GetProperty("assessmentStatus").GetString());
        Assert.Equal(refreshed.GetProperty("answer").GetString(), projected.GetProperty("answer").GetString());
        Assert.Contains(projected.GetProperty("evidence").EnumerateArray(), evidence => evidence.GetString()!.StartsWith(relativeB + ":", StringComparison.Ordinal));
        Run(restarted, "definition", "status");
        foreach (var snapshot in snapshots)
        {
            Assert.Equal(snapshot.Value is not null, File.Exists(snapshot.Key));
            if (snapshot.Value is not null) Assert.Equal(snapshot.Value, File.ReadAllBytes(snapshot.Key));
        }
        Run(restarted, "technical-intent", "init");
        Assert.Equal("unchanged", Run(restarted, "technical-intent", "questions", "init").GetProperty("status").GetString());
        foreach (var snapshot in snapshots)
            if (snapshot.Value is not null) Assert.Equal(snapshot.Value, File.ReadAllBytes(snapshot.Key));
        Assert.Equal(originalA, File.ReadAllBytes(sourceA));
        Assert.Equal(originalB, File.ReadAllBytes(sourceB));
        Assert.Equal(adoptedA, File.ReadAllBytes(workingA));
    }

    [Fact]
    public void AcceptedTechnicalInput_InitializingHistoricalReplacementRequiresExplicitReadoption()
    {
        using var repository = TemporaryRepository.Create();
        using var sources = TemporaryRepository.Create();
        using var application = CreateApplication();
        Assert.Equal(0, Invoke(application, ["workspace", "init", "--repo", repository.Path, "--root", "docs/cis",
            "--ecosystem", "sample", "--product", "sample", "--yes", "--format", "json"]).ExitCode);
        JsonElement Run(CisApplication app, params string[] args)
        {
            var result = Invoke(app, [.. args, "--workspace", repository.Path, "--format", "json"]);
            Assert.True(result.ExitCode == 0, result.Output + result.Error);
            using var document = JsonDocument.Parse(result.Output);
            return document.RootElement.Clone();
        }
        JsonElement Store(CisApplication app, bool requireCurrent)
        {
            var result = Invoke(app, ["technical-intent", "questions", "status", "--workspace", repository.Path, "--format", "json"]);
            using var document = JsonDocument.Parse(result.Output);
            if (requireCurrent)
            {
                Assert.True(result.ExitCode == 0, result.Output + result.Error);
                Assert.Empty(document.RootElement.GetProperty("errors").EnumerateArray());
            }
            else
            {
                Assert.Contains(result.ExitCode, new[] { 0, 2, 5 });
                Assert.All(document.RootElement.GetProperty("errors").EnumerateArray(),
                    error => Assert.Contains("conflict", error.GetString(), StringComparison.OrdinalIgnoreCase));
            }
            return document.RootElement.GetProperty("questions").EnumerateArray()
                .Single(question => question.GetProperty("id").GetString() == "TI-Q-006").Clone();
        }
        static string Approval(string path) => string.Join("\n", Regex.Matches(File.ReadAllText(path),
            @"(?m)^\s*(?:approved_by|approved_at|approval_reason|approved_content_hash):[^\r\n]*")
            .Select(match => match.Value.Trim()));

        Run(application, "graph", "build");
        Run(application, "brd", "init", "--title", "Historical source replacement fixture");
        var brdPath = Path.Combine(repository.Path, "docs/cis/specs/business-requirements.md");
        var brd = Regex.Replace(File.ReadAllText(brdPath), "(?m)^TODO: Complete .+$", "Stakeholders recorded complete and testable requirements.");
        brd = Regex.Replace(brd, "(?ms)^## Functional requirements\\s*$.*?(?=^## )",
            "## Functional requirements\n\n| ID | Requirement | Priority | Acceptance intent |\n| --- | --- | --- | --- |\n| BRD-FR-001 | A user shall view the product. | Must | The product view is available. |\n\n");
        File.WriteAllText(brdPath, brd + "\n## Current-phase exclusions\n\nAI and learned models are excluded from this phase.\n");
        Run(application, "graph", "build");
        Run(application, "brd", "approve", "--reviewer", "Fixture product owner", "--reason", "Accept requirements that permit either supported primary store.");
        var technicalA = AuthoritativeTechnicalSource.Replace("#### Runtime architecture",
            "| Models | AI and learned models are explicitly excluded from this phase. |\n#### Runtime architecture", StringComparison.Ordinal);
        var historicalOriginal = Path.Combine(sources.Path, "Historical Technical Intent.md");
        File.WriteAllText(historicalOriginal, technicalA.Replace("primary data store is PostgreSQL", "primary data store is SQLite", StringComparison.Ordinal));
        var originalHistoricalBytes = File.ReadAllBytes(historicalOriginal);
        const string historicalReviewer = "Fixture historical technical owner";
        const string historicalReason = "Adopt the SQLite technical baseline for this synthetic product.";
        Run(application, "definition", "documents", "--role", "technical", "--source", historicalOriginal);
        var relativeHistorical = CisProductDocumentPaths.Read(repository.Path)["technical"];
        var workingHistorical = Path.Combine(repository.Path, relativeHistorical);
        Run(application, "technical-intent", "init");
        Run(application, "technical-intent", "approve", "--reviewer", historicalReviewer, "--reason", historicalReason);
        Run(application, "technical-intent", "questions", "init");
        var historicalApproval = Approval(workingHistorical);
        Assert.Contains(historicalReviewer, historicalApproval, StringComparison.Ordinal);
        Assert.Contains(historicalReason, historicalApproval, StringComparison.Ordinal);
        Assert.Equal("Active", Run(application, "technical-intent", "init").GetProperty("validation").GetProperty("effectiveStatus").GetString());
        Run(application, "technical-intent", "questions", "init");
        Run(application, "technical-intent", "init");
        Assert.Equal(historicalApproval, Approval(workingHistorical));
        Assert.Contains("SQLite", Store(application, true).GetProperty("answer").GetString(), StringComparison.Ordinal);
        var historicalApprovedBytes = File.ReadAllBytes(workingHistorical);
        var historicalApprovedSource = Path.Combine(sources.Path, "Approved Historical Technical Intent.md");
        File.WriteAllBytes(historicalApprovedSource, historicalApprovedBytes);

        var sourceA = Path.Combine(sources.Path, "Current Technical Intent.md");
        File.WriteAllText(sourceA, technicalA); var originalA = File.ReadAllBytes(sourceA);
        Run(application, "definition", "documents", "--role", "technical", "--source", sourceA);
        var relativeA = CisProductDocumentPaths.Read(repository.Path)["technical"];
        var workingA = Path.Combine(repository.Path, relativeA);
        Run(application, "technical-intent", "init");
        Run(application, "technical-intent", "approve", "--reviewer", "Fixture current technical owner", "--reason", "Explicitly replace the historical store with PostgreSQL.");
        Run(application, "technical-intent", "questions", "init");
        var approvalA = Approval(workingA);
        Assert.Equal("Active", Run(application, "technical-intent", "init").GetProperty("validation").GetProperty("effectiveStatus").GetString());
        Assert.Equal(approvalA, Approval(workingA));
        Run(application, "technical-intent", "questions", "init");
        var acceptedA = Store(application, true);
        Assert.Equal("accepted-document", acceptedA.GetProperty("resolutionSource").GetString());
        Assert.Equal("Current", acceptedA.GetProperty("assessmentStatus").GetString());
        Assert.Contains("PostgreSQL", acceptedA.GetProperty("answer").GetString(), StringComparison.Ordinal);
        Assert.Contains(acceptedA.GetProperty("evidence").EnumerateArray(), evidence => evidence.GetString()!.StartsWith(relativeA + ":", StringComparison.Ordinal));
        Assert.Contains(acceptedA.GetProperty("sourceFactAuthority").GetProperty("basis").EnumerateObject(), entry => entry.Name == relativeA);
        var adoptedABytes = File.ReadAllBytes(workingA);

        Run(application, "definition", "documents", "--role", "technical", "--source", historicalApprovedSource);
        var relativeB = CisProductDocumentPaths.Read(repository.Path)["technical"];
        var workingB = Path.Combine(repository.Path, relativeB);
        Assert.NotEqual(relativeHistorical, relativeB); Assert.NotEqual(relativeA, relativeB);
        Assert.Equal(historicalApprovedBytes, File.ReadAllBytes(workingB));
        Run(application, "technical-intent", "init");
        Run(application, "technical-intent", "init");
        using var restarted = CreateApplication();
        Run(restarted, "technical-intent", "init");
        var statePaths = new[] { workingHistorical, workingA, workingB, historicalOriginal, historicalApprovedSource, sourceA, brdPath,
            Path.Combine(repository.Path, "docs/cis/catalog.yml"), Path.Combine(repository.Path, "docs/cis/specs/technical-intent-questionnaire.md"),
            Path.Combine(repository.Path, CisProductDocumentPaths.SelectionFile), Path.Combine(repository.Path, ".cis/local/definition-wizard/session.json") };
        var pendingSnapshots = statePaths.ToDictionary(path => path, path => File.Exists(path) ? File.ReadAllBytes(path) : null);
        var pending = Store(restarted, false);
        Assert.Equal(acceptedA.GetProperty("answer").GetString(), pending.GetProperty("answer").GetString());
        Assert.Equal(acceptedA.GetProperty("evidence").GetRawText(), pending.GetProperty("evidence").GetRawText());
        Assert.Equal(acceptedA.GetProperty("sourceFactAuthority").GetProperty("basis").GetRawText(),
            pending.GetProperty("sourceFactAuthority").GetProperty("basis").GetRawText());
        Assert.Equal("accepted-document", pending.GetProperty("resolutionSource").GetString());
        Assert.NotEqual("Current", pending.GetProperty("assessmentStatus").GetString());
        Assert.DoesNotContain("SQLite", pending.GetProperty("answer").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain(pending.GetProperty("evidence").EnumerateArray(), evidence => evidence.GetString()!.StartsWith(relativeB + ":", StringComparison.Ordinal));
        Assert.NotEqual("Active", Run(restarted, "technical-intent", "status").GetProperty("validation").GetProperty("effectiveStatus").GetString());
        Run(restarted, "technical-intent", "status"); Store(restarted, false);
        foreach (var snapshot in pendingSnapshots)
        {
            Assert.Equal(snapshot.Value is not null, File.Exists(snapshot.Key));
            if (snapshot.Value is not null) Assert.Equal(snapshot.Value, File.ReadAllBytes(snapshot.Key));
        }

        // Reusing the historical identity and reason must still perform the new adoption.
        var approvedB = Run(restarted, "technical-intent", "approve", "--reviewer", historicalReviewer, "--reason", historicalReason);
        Assert.Equal("approved", approvedB.GetProperty("status").GetString());
        Assert.True(approvedB.GetProperty("applied").GetBoolean());
        Assert.Equal(relativeB, approvedB.GetProperty("canonicalPath").GetString());
        Assert.Equal("Active", approvedB.GetProperty("validation").GetProperty("effectiveStatus").GetString());
        var directlyAdopted = Store(restarted, true);
        Run(restarted, "technical-intent", "questions", "init");
        var adoptedB = Store(restarted, true);
        foreach (var effective in new[] { directlyAdopted, adoptedB })
        {
            Assert.Equal("Derived", effective.GetProperty("status").GetString());
            Assert.Equal("accepted-document", effective.GetProperty("resolutionSource").GetString());
            Assert.Equal("Current", effective.GetProperty("assessmentStatus").GetString());
            Assert.Contains("SQLite", effective.GetProperty("answer").GetString(), StringComparison.Ordinal);
            Assert.DoesNotContain("PostgreSQL", effective.GetProperty("answer").GetString(), StringComparison.Ordinal);
            Assert.Contains(effective.GetProperty("evidence").EnumerateArray(), evidence => evidence.GetString()!.StartsWith(relativeB + ":", StringComparison.Ordinal));
            Assert.DoesNotContain(effective.GetProperty("evidence").EnumerateArray(), evidence => evidence.GetString()!.StartsWith(relativeA + ":", StringComparison.Ordinal));
            var authority = effective.GetProperty("sourceFactAuthority");
            Assert.Equal("Current", authority.GetProperty("assessmentStatus").GetString());
            Assert.Contains(authority.GetProperty("basis").EnumerateObject(), entry => entry.Name == relativeB);
            Assert.DoesNotContain(authority.GetProperty("basis").EnumerateObject(), entry => entry.Name == relativeA || entry.Name == relativeHistorical);
            Assert.Equal(JsonValueKind.Null, effective.GetProperty("answeredBy").ValueKind);
            Assert.Equal(JsonValueKind.Null, effective.GetProperty("answeredAtUtc").ValueKind);
        }
        var approvalB = Approval(workingB);
        Assert.Contains(historicalReviewer, approvalB, StringComparison.Ordinal);
        Assert.Contains(historicalReason, approvalB, StringComparison.Ordinal);
        Assert.Equal("Active", Run(restarted, "technical-intent", "init").GetProperty("validation").GetProperty("effectiveStatus").GetString());
        Run(restarted, "technical-intent", "questions", "init");
        Assert.Equal(approvalB, Approval(workingB));
        var completedSnapshots = statePaths.ToDictionary(path => path, path => File.Exists(path) ? File.ReadAllBytes(path) : null);
        using var completed = CreateApplication();
        Run(completed, "technical-intent", "status");
        var resumed = Store(completed, true);
        Assert.Equal(adoptedB.GetProperty("answer").GetString(), resumed.GetProperty("answer").GetString());
        Assert.Equal(adoptedB.GetProperty("sourceFactAuthority").GetRawText(), resumed.GetProperty("sourceFactAuthority").GetRawText());
        Run(completed, "definition", "status");
        foreach (var snapshot in completedSnapshots)
        {
            Assert.Equal(snapshot.Value is not null, File.Exists(snapshot.Key));
            if (snapshot.Value is not null) Assert.Equal(snapshot.Value, File.ReadAllBytes(snapshot.Key));
        }
        Run(completed, "technical-intent", "init");
        Assert.Equal("unchanged", Run(completed, "technical-intent", "questions", "init").GetProperty("status").GetString());
        Assert.Equal("unchanged", Run(completed, "technical-intent", "approve", "--reviewer", historicalReviewer, "--reason", historicalReason).GetProperty("status").GetString());
        foreach (var snapshot in completedSnapshots)
            if (snapshot.Value is not null) Assert.Equal(snapshot.Value, File.ReadAllBytes(snapshot.Key));
        Assert.Equal(originalHistoricalBytes, File.ReadAllBytes(historicalOriginal));
        Assert.Equal(historicalApprovedBytes, File.ReadAllBytes(historicalApprovedSource));
        Assert.Equal(historicalApprovedBytes, File.ReadAllBytes(workingHistorical));
        Assert.Equal(adoptedABytes, File.ReadAllBytes(workingA));
        Assert.Equal(originalA, File.ReadAllBytes(sourceA));
    }
}

using Cis.Host;
using Cis.Modules.Repository;
using System.IO.Compression;
using System.Net;

namespace Cis.Modules.Skills.Tests;

public sealed class SkillValidationServiceTests
{
    [Fact]
    public void Commands_AreRegistered()
    {
        using var application = new CisHostBuilder()
            .AddModule(new RepositoryModule())
            .AddModule(new SkillsModule())
            .Build();

        Assert.Equal(0, application.Invoke(["skills", "inventory", "--help"]));
        Assert.Equal(0, application.Invoke(["skills", "validate", "--help"]));
        Assert.Equal(0, application.Invoke(["skills", "import", "--help"]));
        Assert.Equal(0, application.Invoke(["skills", "audit", "--help"]));
    }

    [Fact]
    public void Validate_AcceptsPortableSkill()
    {
        using var repository = TemporaryRepository.CreateInitialized();
        repository.Write(".github/skills/example-skill/SKILL.md", """
            ---
            name: example-skill
            description: Perform an example workflow. Use when an example is required.
            ---

            # Example skill

            Follow the reviewed workflow and preserve evidence.
            """);

        var result = CreateService().Validate(repository.Path, strict: true, fix: true);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("valid", result.Status);
        Assert.False(result.Applied);
        var skill = Assert.Single(result.Skills);
        Assert.Equal("example-skill", skill.Name);
    }

    [Fact]
    public void Validate_ReportsNameDescriptionAndBrokenLinkProblems()
    {
        using var repository = TemporaryRepository.CreateInitialized();
        repository.Write(".github/skills/example-skill/SKILL.md", """
            ---
            name: different-name
            description: Example workflow without a trigger.
            compatibility: local
            ---

            # Example skill

            Read [missing guidance](references/missing.md).
            """);

        var result = CreateService().Validate(repository.Path, strict: false);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains(result.Diagnostics, item => item.Code == "CIS-SKILL-NAME-004");
        Assert.Contains(result.Diagnostics, item => item.Code == "CIS-SKILL-YAML-003");
        Assert.Contains(result.Diagnostics, item => item.Code == "CIS-SKILL-LINK-001");
    }

    [Fact]
    public void Validate_StrictTreatsWarningsAsFailure()
    {
        using var repository = TemporaryRepository.CreateInitialized();
        repository.Write(".github/skills/example-skill/SKILL.md", """
            ---
            name: example-skill
            description: Example workflow without an explicit trigger.
            compatibility: local
            ---

            # Example skill

            Follow the workflow.
            """);

        var normal = CreateService().Validate(repository.Path, strict: false);
        var strict = CreateService().Validate(repository.Path, strict: true);

        Assert.Equal(0, normal.ExitCode);
        Assert.Equal("valid-with-warnings", normal.Status);
        Assert.Equal(2, strict.ExitCode);
    }

    [Fact]
    public void Validate_FixAddsMissingYamlAndHeading()
    {
        using var repository = TemporaryRepository.CreateInitialized();
        repository.Write(
            ".github/skills/example-skill/SKILL.md",
            "Follow the existing repository workflow and preserve its evidence.\n");

        var result = CreateService().Validate(repository.Path, strict: true, fix: true);

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.Applied);
        Assert.Equal([".github/skills/example-skill/SKILL.md"], result.FixedPaths);
        var content = repository.Read(".github/skills/example-skill/SKILL.md");
        Assert.StartsWith("---\nname: example-skill\ndescription:", content, StringComparison.Ordinal);
        Assert.Contains("# Example Skill", content, StringComparison.Ordinal);
        Assert.Contains("Follow the existing repository workflow", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_FixAddsMissingYamlFieldsAndHeadingWithoutRemovingMetadata()
    {
        using var repository = TemporaryRepository.CreateInitialized();
        repository.Write(".github/skills/example-skill/SKILL.md", """
            ---
            compatibility: local
            ---

            Follow the workflow.
            """);

        var result = CreateService().Validate(repository.Path, strict: false, fix: true);

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.Applied);
        Assert.Equal("valid-with-warnings", result.Status);
        var content = repository.Read(".github/skills/example-skill/SKILL.md");
        Assert.Contains("compatibility: local", content, StringComparison.Ordinal);
        Assert.Contains("name: example-skill", content, StringComparison.Ordinal);
        Assert.Contains("description:", content, StringComparison.Ordinal);
        Assert.Contains("# Example Skill", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_FixDoesNotRewriteMalformedYaml()
    {
        using var repository = TemporaryRepository.CreateInitialized();
        const string malformed = "---\nname: [broken\n---\n\nNo heading.\n";
        repository.Write(".github/skills/example-skill/SKILL.md", malformed);

        var result = CreateService().Validate(repository.Path, strict: false, fix: true);

        Assert.Equal(2, result.ExitCode);
        Assert.False(result.Applied);
        Assert.Contains(result.Diagnostics, item => item.Code == "CIS-SKILL-YAML-002");
        Assert.Equal(malformed, repository.Read(".github/skills/example-skill/SKILL.md"));
    }

    [Fact]
    public void Import_LocalBundleRequiresConfirmationAndIsIdempotent()
    {
        using var source = TemporaryRepository.CreateInitialized();
        using var target = TemporaryRepository.CreateInitialized();
        source.Write("skills/example-skill/SKILL.md", PortableSkill("example-skill"));
        source.Write("skills/example-skill/references/example.md", "# Example reference\n");
        var service = CreateImportService();

        var dryRun = service.Import(new SkillImportRequest(
            target.Path, [System.IO.Path.Combine(source.Path, "skills")],
            DryRun: true, Confirmed: false, Fix: false, Strict: true));
        Assert.Equal("dry-run", dryRun.Status);
        Assert.False(File.Exists(System.IO.Path.Combine(target.Path, ".github", "skills", "example-skill", "SKILL.md")));
        var confirmation = service.Import(new SkillImportRequest(
            target.Path, [System.IO.Path.Combine(source.Path, "skills")],
            DryRun: false, Confirmed: false, Fix: false, Strict: true));
        var imported = service.Import(new SkillImportRequest(
            target.Path, [System.IO.Path.Combine(source.Path, "skills")],
            DryRun: false, Confirmed: true, Fix: false, Strict: true));
        var repeated = service.Import(new SkillImportRequest(
            target.Path, [System.IO.Path.Combine(source.Path, "skills")],
            DryRun: false, Confirmed: true, Fix: false, Strict: true));

        Assert.Equal(3, confirmation.ExitCode);
        Assert.True(confirmation.ConfirmationRequired);
        Assert.Equal("imported", imported.Status);
        Assert.True(imported.Applied);
        Assert.True(File.Exists(System.IO.Path.Combine(target.Path, ".github", "skills", "example-skill", "references", "example.md")));
        Assert.True(File.Exists(System.IO.Path.Combine(target.Path, ".cis", "local", "skills", "index.json")));
        Assert.Equal("unchanged", repeated.Status);
        Assert.False(repeated.Applied);
    }

    [Fact]
    public void Import_GitHubTreeUrlDownloadsArchiveAndRespectsSubpath()
    {
        using var target = TemporaryRepository.CreateInitialized();
        var archive = CreateZip(new Dictionary<string, string>
        {
            ["skills-main/catalog/example-skill/SKILL.md"] = PortableSkill("example-skill"),
            ["skills-main/ignored/other-skill/SKILL.md"] = PortableSkill("other-skill"),
        });
        var handler = new ArchiveHandler(archive);
        var service = CreateImportService(new HttpClient(handler));

        var result = service.Import(new SkillImportRequest(
            target.Path,
            ["https://github.com/example/skills/tree/main/catalog"],
            DryRun: true,
            Confirmed: false,
            Fix: false,
            Strict: true));

        Assert.Equal(0, result.ExitCode);
        var skill = Assert.Single(result.Skills);
        Assert.Equal("example-skill", skill.Name);
        Assert.Equal("https://github.com/example/skills/archive/main.zip", handler.RequestUri?.AbsoluteUri);
    }

    [Fact]
    public void Import_ExcludesQuarantinedSkillBundlesFromDiscovery()
    {
        using var source = TemporaryRepository.CreateInitialized();
        using var target = TemporaryRepository.CreateInitialized();
        source.Write(".github/skills/active-skill/SKILL.md", PortableSkill("active-skill"));
        source.Write(".github/skills-quarantine/retired-skill/SKILL.md", PortableSkill("retired-skill"));

        var result = CreateImportService().Import(new SkillImportRequest(
            target.Path, [source.Path], DryRun: true, Confirmed: false, Fix: false, Strict: true));

        Assert.Equal(0, result.ExitCode);
        var skill = Assert.Single(result.Skills);
        Assert.Equal("active-skill", skill.Name);
    }

    [Fact]
    public void Import_RejectsDifferentExistingContent()
    {
        using var source = TemporaryRepository.CreateInitialized();
        using var target = TemporaryRepository.CreateInitialized();
        source.Write("skills/example-skill/SKILL.md", PortableSkill("example-skill"));
        target.Write(".github/skills/example-skill/SKILL.md", PortableSkill("example-skill") + "\nDifferent.\n");

        var result = CreateImportService().Import(new SkillImportRequest(
            target.Path, [System.IO.Path.Combine(source.Path, "skills")],
            DryRun: false, Confirmed: true, Fix: false, Strict: true));

        Assert.Equal(4, result.ExitCode);
        Assert.Equal("conflict", result.Status);
        Assert.False(result.Applied);
    }

    [Fact]
    public void Audit_IsolatesExactDuplicateBodiesAndWritesDerivedReports()
    {
        using var repository = TemporaryRepository.CreateInitialized();
        repository.Write(".github/skills/first-skill/SKILL.md", PortableSkill("first-skill"));
        repository.Write(".github/skills/second-skill/SKILL.md", PortableSkill("second-skill"));
        var resolver = new CisRepositoryContextResolver();
        var service = new SkillAuditService(resolver, new SkillValidationService(resolver));

        var result = service.Audit(new SkillAuditRequest(
            repository.Path, Strict: false, DisableLlm: true, Fix: false, Model: null, MaximumPairs: 100));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("findings", result.Status);
        Assert.Equal(1, result.DuplicateCount);
        Assert.Contains(result.Findings, finding => finding.Kind == "duplicate"
            && finding.Method == "deterministic"
            && finding.Skills.SequenceEqual(["first-skill", "second-skill"]));
        Assert.True(File.Exists(System.IO.Path.Combine(repository.Path, ".cis", "local", "skills", "audit.json")));
        Assert.True(File.Exists(System.IO.Path.Combine(repository.Path, ".cis", "local", "skills", "audit.md")));
    }

    [Fact]
    public void Audit_UsesAvailableLocalModelAndIsolatesReportedConflictAsAdvisory()
    {
        using var repository = TemporaryRepository.CreateInitialized();
        repository.Write(".github/skills/deploy-app/SKILL.md", Skill(
            "deploy-app", "Deploy a production application release using approved rollout controls.",
            "Apply the production release automatically after validation."));
        repository.Write(".github/skills/release-app/SKILL.md", Skill(
            "release-app", "Review a production application release using approved rollout controls.",
            "Never apply the production release automatically after validation."));
        var resolver = new CisRepositoryContextResolver();
        var generation = new FixedGenerationService("""
            {"findings":[{"left":"deploy-app","right":"release-app","kind":"conflict","confidence":0.97,"summary":"Automatic release authority is contradictory.","evidence":["Deploy requires automatic apply.","Release prohibits automatic apply."]}]}
            """);
        var service = new SkillAuditService(resolver, new SkillValidationService(resolver), generation);

        var result = service.Audit(new SkillAuditRequest(
            repository.Path, Strict: false, DisableLlm: false, Fix: false, Model: null, MaximumPairs: 100));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("completed", result.LlmStatus);
        Assert.Equal("qwen-test:1.5b", result.Model);
        var conflict = Assert.Single(result.Findings, finding => finding.Kind == "conflict");
        Assert.Equal("local-llm", conflict.Method);
        Assert.Equal("warning", conflict.Severity);
        Assert.Equal(0.97, conflict.Confidence);

        var strict = service.Audit(new SkillAuditRequest(
            repository.Path, Strict: true, DisableLlm: false, Fix: false, Model: null, MaximumPairs: 100));
        Assert.Equal(5, strict.ExitCode);

        var fixedResult = service.Audit(new SkillAuditRequest(
            repository.Path, Strict: true, DisableLlm: false, Fix: true, Model: null, MaximumPairs: 100));
        Assert.Equal(0, fixedResult.ExitCode);
        Assert.Equal(["deploy-app", "release-app"], fixedResult.QuarantinedSkills);
        Assert.True(File.Exists(System.IO.Path.Combine(
            repository.Path, ".github", "skills-quarantine", "deploy-app", "SKILL.md")));
        Assert.True(File.Exists(System.IO.Path.Combine(
            repository.Path, ".github", "skills-quarantine", "release-app", "SKILL.md")));
    }

    [Fact]
    public void Audit_OmitsLocalConflictWithoutRequiredAndProhibitedDirectiveEvidence()
    {
        using var repository = TemporaryRepository.CreateInitialized();
        repository.Write(".github/skills/unit-tests/SKILL.md", Skill(
            "unit-tests", "Test affected application behavior with focused verification evidence.",
            "Add focused unit tests for changed behavior."));
        repository.Write(".github/skills/architecture-tests/SKILL.md", Skill(
            "architecture-tests", "Test affected application architecture with focused verification evidence.",
            "Add architecture tests for changed boundaries."));
        var resolver = new CisRepositoryContextResolver();
        var generation = new FixedGenerationService("""
            {"findings":[{"left":"unit-tests","right":"architecture-tests","kind":"conflict","confidence":0.99,"summary":"The test workflows differ.","evidence":["One adds unit tests.","One adds architecture tests."]}]}
            """);
        var service = new SkillAuditService(resolver, new SkillValidationService(resolver), generation);

        var result = service.Audit(new SkillAuditRequest(
            repository.Path, Strict: false, DisableLlm: false, Fix: false, Model: null, MaximumPairs: 100));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(0, result.ConflictCount);
        Assert.Empty(result.Findings);
        Assert.Contains(result.Warnings, warning => warning.Contains("unsubstantiated conflict", StringComparison.Ordinal));
    }

    [Fact]
    public void Audit_ReportsPartialWhenAModelResponseIsNotParseableJson()
    {
        using var repository = TemporaryRepository.CreateInitialized();
        repository.Write(".github/skills/unit-tests/SKILL.md", Skill(
            "unit-tests", "Test affected application behavior with focused verification evidence.",
            "Add focused unit tests for changed behavior."));
        repository.Write(".github/skills/architecture-tests/SKILL.md", Skill(
            "architecture-tests", "Test affected application architecture with focused verification evidence.",
            "Add architecture tests for changed boundaries."));
        var resolver = new CisRepositoryContextResolver();
        var service = new SkillAuditService(
            resolver,
            new SkillValidationService(resolver),
            new FixedGenerationService("```json"));

        var result = service.Audit(new SkillAuditRequest(
            repository.Path, Strict: false, DisableLlm: false, Fix: false, Model: null, MaximumPairs: 100));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("partial", result.LlmStatus);
        Assert.Contains(result.Warnings, warning => warning.Contains("invalid JSON", StringComparison.Ordinal));
    }

    [Fact]
    public void Audit_OmitsComplementaryScopesMisreportedAsOverlap()
    {
        using var repository = TemporaryRepository.CreateInitialized();
        repository.Write(".github/skills/change-orchestration/SKILL.md", Skill(
            "change-orchestration", "Orchestrate impact planning and evidence review for a governed change dossier.",
            "Route the dossier through specialist impact review before planning."));
        repository.Write(".github/skills/impact-specialist/SKILL.md", Skill(
            "impact-specialist", "Review impact evidence and findings for a governed change dossier.",
            "Assess impact findings and return evidence to the orchestrator."));
        var resolver = new CisRepositoryContextResolver();
        var generation = new FixedGenerationService("""
            {"findings":[{"left":"change-orchestration","right":"impact-specialist","kind":"overlap","confidence":0.95,"summary":"Both address impact planning but differ in scope.","evidence":["The first skill orchestrates dossier planning and routes specialist review.","The second skill reviews impact evidence while the other orchestrates."]}]}
            """);
        var service = new SkillAuditService(resolver, new SkillValidationService(resolver), generation);

        var result = service.Audit(new SkillAuditRequest(
            repository.Path, Strict: false, DisableLlm: false, Fix: false, Model: null, MaximumPairs: 100));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Audit_FixQuarantinesRedundantDeterministicDuplicateAndKeepsOneActiveCopy()
    {
        using var repository = TemporaryRepository.CreateInitialized();
        repository.Write(".github/skills/first-skill/SKILL.md", PortableSkill("first-skill"));
        repository.Write(".github/skills/second-skill/SKILL.md", PortableSkill("second-skill"));
        var resolver = new CisRepositoryContextResolver();
        var validation = new SkillValidationService(resolver);
        var service = new SkillAuditService(resolver, validation);

        var result = service.Audit(new SkillAuditRequest(
            repository.Path, Strict: true, DisableLlm: true, Fix: true, Model: null, MaximumPairs: 100));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("quarantined", result.Status);
        Assert.Equal(["second-skill"], result.QuarantinedSkills);
        Assert.True(File.Exists(System.IO.Path.Combine(
            repository.Path, ".github", "skills-quarantine", "second-skill", "SKILL.md")));
        Assert.False(Directory.Exists(System.IO.Path.Combine(
            repository.Path, ".github", "skills", "second-skill")));
        Assert.Single(validation.Validate(repository.Path, strict: true).Skills);
    }

    [Fact]
    public void Audit_FixDoesNotMoveAnythingWhenQuarantineDestinationExists()
    {
        using var repository = TemporaryRepository.CreateInitialized();
        repository.Write(".github/skills/first-skill/SKILL.md", PortableSkill("first-skill"));
        repository.Write(".github/skills/second-skill/SKILL.md", PortableSkill("second-skill"));
        repository.Write(".github/skills-quarantine/second-skill/SKILL.md", PortableSkill("second-skill"));
        var resolver = new CisRepositoryContextResolver();
        var service = new SkillAuditService(resolver, new SkillValidationService(resolver));

        var result = service.Audit(new SkillAuditRequest(
            repository.Path, Strict: false, DisableLlm: true, Fix: true, Model: null, MaximumPairs: 100));

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("invalid", result.Status);
        Assert.Empty(result.QuarantinedSkills);
        Assert.True(Directory.Exists(System.IO.Path.Combine(repository.Path, ".github", "skills", "first-skill")));
        Assert.True(Directory.Exists(System.IO.Path.Combine(repository.Path, ".github", "skills", "second-skill")));
        Assert.Contains(result.Errors, error => error.Contains("already exists", StringComparison.Ordinal));
    }

    [Fact]
    public void Audit_FallsBackToConfiguredRemoteProviderWhenLocalIsUnavailable()
    {
        using var repository = TemporaryRepository.CreateInitialized();
        repository.Write(".github/skills/first-skill/SKILL.md", Skill(
            "first-skill", "Verify application releases with compatibility evidence.", "Verify release compatibility."));
        repository.Write(".github/skills/second-skill/SKILL.md", Skill(
            "second-skill", "Review application releases with compatibility evidence.", "Review release compatibility."));
        var resolver = new CisRepositoryContextResolver();
        var generation = new FixedGenerationService("""
            {"findings":[{"left":"first-skill","right":"second-skill","kind":"overlap","confidence":0.91,"summary":"Both own release compatibility verification.","evidence":["The first verifies application release compatibility evidence.","The second reviews application release compatibility evidence."],}],}
            """, isLocal: false);
        var service = new SkillAuditService(resolver, new SkillValidationService(resolver), generation);

        var result = service.Audit(new SkillAuditRequest(
            repository.Path, Strict: false, DisableLlm: false, Fix: false, Model: null, MaximumPairs: 100));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("remote-test", result.Provider);
        Assert.Contains(result.Findings, finding => finding.Method == "remote-llm");
        Assert.NotNull(generation.LastRequest);
        Assert.True(generation.LastRequest.AllowRemote);
    }

    private static SkillValidationService CreateService()
        => new(new CisRepositoryContextResolver());

    private static SkillImportService CreateImportService(HttpClient? client = null)
    {
        var resolver = new CisRepositoryContextResolver();
        return new SkillImportService(resolver, new SkillValidationService(resolver), client);
    }

    private static string PortableSkill(string name)
        => $"---\nname: {name}\ndescription: Perform the {name} workflow. Use when this imported skill applies.\n---\n\n# {name}\n\nFollow the workflow.\n";

    private static string Skill(string name, string description, string body)
        => $"---\nname: {name}\ndescription: {description}\n---\n\n# {name}\n\n{body}\n";

    private static byte[] CreateZip(IReadOnlyDictionary<string, string> files)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files)
            {
                var entry = archive.CreateEntry(file.Key);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(file.Value);
            }
        }

        return output.ToArray();
    }

    private sealed class ArchiveHandler(byte[] archive) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(archive),
            });
        }
    }

    private sealed class FixedGenerationService(string response, bool isLocal = true) : Cis.Abstractions.ICisTextGenerationService
    {
        private string ProviderName => isLocal ? "ollama" : "remote-test";

        private string ModelName => isLocal ? "qwen-test:1.5b" : "remote-test-model";

        public Cis.Abstractions.CisTextGenerationRequest? LastRequest { get; private set; }

        public Cis.Abstractions.CisAiStatus GetStatus()
            => new([new Cis.Abstractions.CisAiProviderStatus(
                ProviderName, "available", isLocal ? "http://localhost:11434" : "https://remote.test", isLocal,
                [new Cis.Abstractions.CisAiModel(ModelName, 1_500_000_000)], null)]);

        public Cis.Abstractions.CisTextGenerationResult Generate(
            Cis.Abstractions.CisTextGenerationRequest request)
        {
            LastRequest = request;
            return new("generated", ProviderName, ModelName, response, null, isLocal);
        }
    }

    private sealed class TemporaryRepository : IDisposable
    {
        private TemporaryRepository(string path) => Path = path;

        public string Path { get; }

        public static TemporaryRepository CreateInitialized()
        {
            var repository = new TemporaryRepository(System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "cis-skills-tests", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(repository.Path);
            repository.Write(".cis/repository.yml", """
                schema_version: 1
                repository:
                  id: skills-test
                documentation_root: docs/cis
                """);
            Directory.CreateDirectory(System.IO.Path.Combine(repository.Path, "docs", "cis"));
            return repository;
        }

        public void Write(string relativePath, string content)
        {
            var path = System.IO.Path.Combine(Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content.Replace("\r\n", "\n", StringComparison.Ordinal));
        }

        public string Read(string relativePath)
            => File.ReadAllText(System.IO.Path.Combine(
                Path,
                relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar)));

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}

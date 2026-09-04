using System.IO.Compression;
using System.Net;
using Cis.Abstractions;
using Cis.Host;
using Cis.Modules.Docs;
using Cis.Modules.Repository;
using Cis.Modules.Standards;

namespace Cis.Modules.Standards.Tests;

public sealed class StandardsGovernanceTests
{
    [Fact]
    public void Commands_AreRegisteredByTheExplicitModule()
    {
        using var application = new CisHostBuilder()
            .AddModule(new RepositoryModule())
            .AddModule(new DocsModule())
            .AddModule(new StandardsModule())
            .Build();

        Assert.Equal(0, application.Invoke(["standards", "inventory", "--help"]));
        Assert.Equal(0, application.Invoke(["standards", "applicable", "--help"]));
        Assert.Equal(0, application.Invoke(["standards", "validate", "--help"]));
        Assert.Equal(0, application.Invoke(["standards", "conformance", "--help"]));
        Assert.Equal(0, application.Invoke(["standards", "import", "--help"]));
        Assert.Equal(0, application.Invoke(["standards", "audit", "--help"]));
        Assert.Equal(0, application.Invoke(["standards", "patterns", "--help"]));
        Assert.Equal(0, application.Invoke(["standards", "infer", "--help"]));
    }

    [Fact]
    public void PatternCatalogue_CombinesKnownPatternsWithCanonicalRepositoryMarkdown()
    {
        using var repository = StandardRepository.Create();
        repository.Write("docs/references/standard-patterns/custom.md", RepositoryPattern("fixture.pattern.csharp.custom"));
        repository.Write("docs/catalog.yml", File.ReadAllText(System.IO.Path.Combine(repository.Path, "docs", "catalog.yml")) +
            "  - id: fixture.pattern.csharp.custom\n    path: docs/references/standard-patterns/custom.md\n    type: standard-inference-pattern\n    status: active\n    authority: canonical\n");
        var graph = PatternGraph(repository.Path);
        var service = new StandardPatternCatalogService(
            new CisRepositoryContextResolver(),
            [new BuiltInStandardPatternProvider()],
            graph);

        var result = service.Inventory(new StandardPatternCatalogRequest(repository.Path, ["csharp"], ApplicableOnly: false, Strict: true));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(12, result.Patterns.Count);
        var custom = Assert.Single(result.Patterns, item => item.Definition.Id == "fixture.pattern.csharp.custom");
        Assert.Equal("repository", custom.Definition.Provider);
        Assert.Equal("canonical", custom.Definition.Authority);
        Assert.Equal("ready", custom.Readiness);
    }

    [Fact]
    public void Infer_MatchesReadyCompilerPatternsAndPreservesCounterexampleEvidenceWithoutCanonicalWrites()
    {
        using var repository = StandardRepository.Create();
        var graph = PatternGraph(repository.Path);
        var catalog = new StandardPatternCatalogService(new CisRepositoryContextResolver(), [new BuiltInStandardPatternProvider()], graph);
        var service = new StandardInferenceService(catalog, graph);

        var result = service.Infer(new StandardInferenceRequest(
            repository.Path,
            ["csharp.modularity.command-registration", "csharp.testing.calls-production-symbol"],
            ["csharp"], [], 10));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(2, result.CandidateCount);
        Assert.All(result.Candidates, candidate => Assert.Equal("Unreviewed", candidate.Status));
        Assert.Contains(result.Evaluations, item => item.PatternId == "csharp.modularity.command-registration" && item.MatchingOccurrences == 3 && item.EligibleOccurrences == 3);
        Assert.Contains(result.Evaluations, item => item.PatternId == "csharp.testing.calls-production-symbol" && item.MatchingOccurrences == 5 && item.EligibleOccurrences == 5);
        Assert.True(File.Exists(System.IO.Path.Combine(repository.Path, ".cis", "local", "standards", "inference", "report.json")));
        Assert.False(Directory.Exists(System.IO.Path.Combine(repository.Path, "docs", "standards", "inferred")));
    }

    [Fact]
    public void PatternCatalogue_ActivatesEveryKnownPatternWhenSemanticGraphCapabilitiesExist()
    {
        using var repository = StandardRepository.Create();
        var capabilities = new[]
        {
            "compiler.symbols", "compiler.calls", "tests.symbols", "tests.calls",
            "compiler.attributes", "compiler.interfaces", "compiler.inheritance", "compiler.external-calls",
            "compiler.generic-arguments", "compiler.invocation-arguments",
            "compiler.generated-markers", "compiler.dataflow",
        };
        var graph = PatternGraph(repository.Path, capabilities);
        var service = new StandardPatternCatalogService(
            new CisRepositoryContextResolver(),
            [new BuiltInStandardPatternProvider()],
            graph);

        var result = service.Inventory(new StandardPatternCatalogRequest(
            repository.Path, ["csharp"], ApplicableOnly: false, Strict: true));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(11, result.Patterns.Count);
        Assert.All(result.Patterns, pattern => Assert.Equal("ready", pattern.Readiness));
    }

    [Fact]
    public void Infer_UsesSemanticEdgesAndPreservesTypedOptionsCounterexamples()
    {
        using var repository = StandardRepository.Create();
        var component = Node("fixture::component::tool", "component", "tooling", "tool", "tool", null,
            new Dictionary<string, string> { ["languages"] = "csharp", ["roles"] = "tooling", ["capabilities"] = "configuration" });
        var options = Node("fixture::symbol::options", "symbol", "method", "options", "OptionsExtensions.Configure<T>()", "src/Composition.cs",
            new Dictionary<string, string> { ["component"] = "external:options", ["external"] = "true", ["qualifiedName"] = "OptionsExtensions.Configure<T>()" }, ["csharp", "compiler-bound", "external"]);
        var direct = Node("fixture::symbol::direct", "symbol", "method", "direct", "IConfiguration.GetValue<T>()", "src/Composition.cs",
            new Dictionary<string, string> { ["component"] = "external:configuration", ["external"] = "true", ["qualifiedName"] = "IConfiguration.GetValue<T>()" }, ["csharp", "compiler-bound", "external"]);
        var nodes = new List<CisGraphNode> { component, options, direct };
        var edges = new List<CisGraphEdge>();
        for (var index = 1; index <= 5; index++)
        {
            var method = Node($"fixture::symbol::configuration-{index}", "symbol", "method", $"configuration-{index}", $"Fixture.Composition.Configure{index}()", $"src/Composition{index}.cs",
                new Dictionary<string, string> { ["component"] = "tool", ["callSemantics"] = "configuration", ["external"] = "false" }, ["csharp", "compiler-bound"]);
            nodes.Add(method);
            edges.Add(index < 5
                ? Edge($"bind-{index}", method.Key, options.Key, "binds-options")
                : Edge("read-5", method.Key, direct.Key, "reads-configuration"));
        }
        var graphDocument = new CisGraphDocument(2, new("build-semantic", "fixture", "abc", false, "complete"), nodes, edges, new(0, 0, 0));
        var manifest = new CisGraphManifest(2, "build-semantic", "fixture", "docs", "abc", false, [], ["cis.csharp.compiler/1", "cis.csharp.semantic-facts/1"]);
        var graph = new FixedGraphSnapshotReader(repository.Path, graphDocument, manifest,
            ["compiler.symbols", "compiler.external-calls", "compiler.generic-arguments"]);
        var catalog = new StandardPatternCatalogService(new CisRepositoryContextResolver(), [new BuiltInStandardPatternProvider()], graph);

        var result = new StandardInferenceService(catalog, graph).Infer(new StandardInferenceRequest(
            repository.Path, ["csharp.configuration.options-binding"], ["csharp"], [], 10));

        Assert.Equal(0, result.ExitCode);
        var evaluation = Assert.Single(result.Evaluations);
        Assert.Equal("candidate", evaluation.Status);
        Assert.Equal(5, evaluation.EligibleOccurrences);
        Assert.Equal(4, evaluation.MatchingOccurrences);
        Assert.Single(evaluation.Counterexamples);
        Assert.Single(result.Candidates);
    }

    [Fact]
    public void Validate_AcceptsCanonicalStandardAndConformanceMapping()
    {
        using var repository = StandardRepository.Create();
        var service = CreateService();

        var result = service.Validate(repository.Path, strict: true);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, result.Standards);
        Assert.Equal(2, result.Rules);
        Assert.Equal(2, result.ConformanceEntries);
    }

    [Fact]
    public void Applicable_FiltersActiveStandardsByTargetAndStack()
    {
        using var repository = StandardRepository.Create();
        var result = CreateService().Applicable(repository.Path, ["backend"], ["csharp"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Single(result.Standards);
        Assert.Equal("fixture:standard:testing", result.Standards[0].Id);
        Assert.Empty(CreateService().Applicable(repository.Path, ["frontend"], ["typescript"]).Standards);
    }

    [Fact]
    public void Validate_RejectsDuplicateRulesMissingSectionsAndUnmappedActiveRules()
    {
        using var repository = StandardRepository.Create();
        repository.Write("docs/standards/second-standard.md", StandardRepository.Standard("fixture:standard:second", includeVerification: false));
        repository.Write("docs/catalog.yml", StandardRepository.Catalog(includeSecond: true));

        var result = CreateService().Validate(repository.Path, strict: false);

        Assert.Equal(5, result.ExitCode);
        Assert.Contains(result.Diagnostics, item => item.Code == "CIS-STD-RULE-004");
        Assert.Contains(result.Diagnostics, item => item.Code == "CIS-STD-DOC-001" && item.Message.Contains("Verification", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, item => item.Code == "CIS-STD-CONF-006" && item.Message.Contains("fixture:standard:second", StringComparison.Ordinal));
    }

    [Fact]
    public void StrictValidation_FailsAdvisoryOnlyEvidenceWithoutCallingItABreach()
    {
        using var repository = StandardRepository.Create(advisory: true);
        var result = CreateService().Validate(repository.Path, strict: true);

        Assert.Equal(5, result.ExitCode);
        var diagnostic = Assert.Single(result.Diagnostics, item => item.Code == "CIS-STD-CONF-004");
        Assert.Equal("warning", diagnostic.Severity);
        Assert.Contains("cannot prove", diagnostic.Remediation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RepositoryInit_SeedsAValidClassificationSelectedParrDerivedApiSet()
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cis-standard-starter-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(System.IO.Path.Combine(root, "src", "Example.Api"));
            File.WriteAllText(
                System.IO.Path.Combine(root, "src", "Example.Api", "Example.Api.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
            File.WriteAllText(
                System.IO.Path.Combine(root, "src", "Example.Api", "Program.cs"),
                "builder.Services.AddControllers(); builder.Services.AddAuthorization(); app.MapControllers();");

            var initialized = new RepositoryInitializer().Initialize(new RepositoryInitRequest(
                root,
                "docs/cis",
                DryRun: false,
                Confirmed: true));
            var validated = CreateService().Validate(root, strict: true);

            Assert.Equal(0, initialized.ExitCode);
            Assert.Equal(0, validated.ExitCode);
            Assert.Equal(8, validated.Standards);
            Assert.Equal(62, validated.Rules);
            Assert.Equal(62, validated.ConformanceEntries);
            Assert.True(File.Exists(System.IO.Path.Combine(root, "docs", "cis", "standards", "api-controller-standard.md")));
            Assert.False(File.Exists(System.IO.Path.Combine(root, "docs", "cis", "standards", "frontend-interaction-standard.md")));
            Assert.Contains(
                "source_repository: PARR",
                File.ReadAllText(System.IO.Path.Combine(root, "docs", "cis", "standards", "api-controller-standard.md")),
                StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Import_PreviewsRequiresConfirmationAppliesCatalogAndConformanceThenIsIdempotent()
    {
        using var repository = StandardRepository.Create();
        repository.Write("incoming/imported-standard.md", StandardRepository.ImportedStandard());
        var service = new StandardImportService(new CisRepositoryContextResolver(), new DocumentationCatalogMerger());
        var source = System.IO.Path.Combine(repository.Path, "incoming", "imported-standard.md");

        var preview = service.Import(new StandardImportRequest(repository.Path, [source], DryRun: true, Confirmed: false, Fix: false, Strict: true));
        var confirmation = service.Import(new StandardImportRequest(repository.Path, [source], DryRun: false, Confirmed: false, Fix: false, Strict: true));
        var applied = service.Import(new StandardImportRequest(repository.Path, [source], DryRun: false, Confirmed: true, Fix: false, Strict: true));
        var repeated = service.Import(new StandardImportRequest(repository.Path, [source], DryRun: false, Confirmed: true, Fix: false, Strict: true));

        Assert.Equal("dry-run", preview.Status);
        Assert.Equal(3, confirmation.ExitCode);
        Assert.True(confirmation.ConfirmationRequired);
        Assert.Equal("imported", applied.Status);
        Assert.True(File.Exists(System.IO.Path.Combine(repository.Path, "docs", "standards", "imported-standard.md")));
        Assert.Contains("fixture:standard:imported", File.ReadAllText(System.IO.Path.Combine(repository.Path, "docs", "catalog.yml")), StringComparison.Ordinal);
        Assert.Contains("IMP-001", File.ReadAllText(System.IO.Path.Combine(repository.Path, "docs", "references", "standards-conformance-matrix.md")), StringComparison.Ordinal);
        Assert.Equal("unchanged", repeated.Status);
        Assert.Equal(0, CreateService().Validate(repository.Path, strict: true).ExitCode);
    }

    [Fact]
    public void ImportFix_RepairsStructureAndAssignsIdsWithoutInventingNormativeSemantics()
    {
        using var repository = StandardRepository.Create();
        repository.Write("incoming/legacy-standard.md", "# Legacy Standard\n\n## Rules\n\nA service MUST be bounded.\n");
        var service = new StandardImportService(new CisRepositoryContextResolver(), new DocumentationCatalogMerger());

        var result = service.Import(new StandardImportRequest(
            repository.Path,
            [System.IO.Path.Combine(repository.Path, "incoming", "legacy-standard.md")],
            DryRun: true, Confirmed: false, Fix: true, Strict: false));

        Assert.Equal(0, result.ExitCode);
        var imported = Assert.Single(result.Standards);
        Assert.True(imported.RuleCount > 0);
        Assert.Contains(result.Warnings, warning => warning.Contains("no rule semantics were generated", StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(System.IO.Path.Combine(repository.Path, "docs", "standards", "legacy-standard.md")));
    }

    [Fact]
    public void Import_GitHubTreeUrlDownloadsArchiveAndRespectsSubpath()
    {
        using var repository = StandardRepository.Create();
        var archive = CreateZip(new Dictionary<string, string>
        {
            ["standards-main/catalog/imported-standard.md"] = StandardRepository.ImportedStandard(),
            ["standards-main/ignored/other-standard.md"] = StandardRepository.ImportedStandard().Replace("fixture:standard:imported", "fixture:standard:other", StringComparison.Ordinal).Replace("IMP-001", "OTH-001", StringComparison.Ordinal),
        });
        var handler = new ArchiveHandler(archive);
        var service = new StandardImportService(new CisRepositoryContextResolver(), new DocumentationCatalogMerger(), new HttpClient(handler));

        var result = service.Import(new StandardImportRequest(repository.Path, ["https://github.com/example/standards/tree/main/catalog"], DryRun: true, Confirmed: false, Fix: false, Strict: true));

        Assert.Equal(0, result.ExitCode);
        Assert.Single(result.Standards);
        Assert.Equal("https://github.com/example/standards/archive/main.zip", handler.RequestUri?.AbsoluteUri);
    }

    [Fact]
    public void Import_RejectsPlainHttpSourcesBeforeSendingARequest()
    {
        using var repository = StandardRepository.Create();
        var handler = new ArchiveHandler(CreateZip(new Dictionary<string, string>()));
        var service = new StandardImportService(new CisRepositoryContextResolver(), new DocumentationCatalogMerger(), new HttpClient(handler));

        var result = service.Import(new StandardImportRequest(repository.Path, ["http://example.test/standards.zip"],
            DryRun: true, Confirmed: false, Fix: false, Strict: true));

        Assert.Equal(2, result.ExitCode);
        Assert.Contains(result.Errors, error => error.Contains("HTTPS", StringComparison.OrdinalIgnoreCase));
        Assert.Null(handler.RequestUri);
    }

    [Fact]
    public void AuditFix_QuarantinesOneDeterministicDuplicateAndPreservesHistoricalCatalogAndMappings()
    {
        using var repository = StandardRepository.Create();
        repository.Write("docs/standards/second-standard.md", StandardRepository.Standard("fixture:standard:second", includeVerification: true).Replace("TEST-", "ALT-", StringComparison.Ordinal));
        repository.Write("docs/catalog.yml", StandardRepository.Catalog(includeSecond: true));
        repository.Write("docs/references/standards-conformance-matrix.md", File.ReadAllText(System.IO.Path.Combine(repository.Path, "docs", "references", "standards-conformance-matrix.md")) +
            "| fixture:standard:second | ALT-001 | src | deterministic | tests | Active | structural |\n| fixture:standard:second | ALT-002 | tests | manual-review | review | Active | proportionate |\n");
        var service = new StandardAuditService(new CisRepositoryContextResolver(), CreateService());

        var result = service.Audit(new StandardAuditRequest(repository.Path, Strict: false, DisableLlm: true, Fix: true, Model: null, MaximumPairs: 10));

        Assert.Equal("quarantined", result.Status);
        Assert.Single(result.QuarantinedStandards);
        var quarantinedId = result.QuarantinedStandards[0];
        Assert.Contains("quarantined-standard", File.ReadAllText(System.IO.Path.Combine(repository.Path, "docs", "catalog.yml")), StringComparison.Ordinal);
        Assert.Contains("> | " + quarantinedId, File.ReadAllText(System.IO.Path.Combine(repository.Path, "docs", "references", "standards-conformance-matrix.md")), StringComparison.Ordinal);
        Assert.True(Directory.EnumerateFiles(System.IO.Path.Combine(repository.Path, "docs", "standards-quarantine")).Any());
        Assert.True(File.Exists(System.IO.Path.Combine(repository.Path, ".cis", "local", "standards", "audit.json")));
    }

    [Fact]
    public void Audit_PrefersLocalThenAllowsConfiguredRemoteFallbackAndKeepsModelFindingsAdvisory()
    {
        using var repository = StandardRepository.Create();
        repository.Write("docs/standards/second-standard.md", StandardRepository.Standard("fixture:standard:second", includeVerification: true).Replace("TEST-", "ALT-", StringComparison.Ordinal).Replace("Code MUST follow the architecture.", "Code MUST NOT follow the architecture.", StringComparison.Ordinal));
        repository.Write("docs/catalog.yml", StandardRepository.Catalog(includeSecond: true));
        repository.Write("docs/references/standards-conformance-matrix.md", File.ReadAllText(System.IO.Path.Combine(repository.Path, "docs", "references", "standards-conformance-matrix.md")) +
            "| fixture:standard:second | ALT-001 | src | manual-review | review | Active | structural |\n| fixture:standard:second | ALT-002 | tests | manual-review | review | Active | proportionate |\n");
        var generation = new FixedGenerationService("{\"findings\":[{\"kind\":\"conflict\",\"confidence\":0.8,\"summary\":\"Candidate contradiction\",\"evidence\":[\"TEST-001 requires code to follow the architecture\",\"ALT-001 says code must not follow the architecture\"],}],}", isLocal: false);
        var service = new StandardAuditService(new CisRepositoryContextResolver(), CreateService(), generation);

        var result = service.Audit(new StandardAuditRequest(repository.Path, Strict: false, DisableLlm: false, Fix: false, Model: null, MaximumPairs: 10));

        Assert.Equal("remote", result.Provider);
        Assert.Contains(result.Warnings, warning => warning.Contains("configured remote", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Findings, finding => finding.Method == "remote-llm" && finding.Severity == "warning");
        Assert.True(generation.LastRequest?.AllowRemote);
    }

    [Fact]
    public void Audit_OmitsBroadTopicOverlapWithoutSharedNormativeResponsibility()
    {
        using var repository = StandardRepository.Create();
        repository.Write("docs/standards/second-standard.md", StandardRepository.Standard("fixture:standard:second", includeVerification: true)
            .Replace("TEST-", "ALT-", StringComparison.Ordinal)
            .Replace("Code MUST follow the architecture.", "Code MUST expose explicit boundaries.", StringComparison.Ordinal)
            .Replace("Changes MUST have proportionate tests.", "Boundaries MUST have named owners.", StringComparison.Ordinal));
        repository.Write("docs/catalog.yml", StandardRepository.Catalog(includeSecond: true));
        repository.Write("docs/references/standards-conformance-matrix.md", File.ReadAllText(System.IO.Path.Combine(repository.Path, "docs", "references", "standards-conformance-matrix.md")) +
            "| fixture:standard:second | ALT-001 | src | manual-review | review | Active | structural |\n| fixture:standard:second | ALT-002 | src | manual-review | review | Active | ownership |\n");
        var generation = new FixedGenerationService("{\"findings\":[{\"kind\":\"overlap\",\"confidence\":0.95,\"summary\":\"Both concern backend code.\",\"evidence\":[\"TEST-001 says code must follow architecture.\",\"ALT-001 says code must expose explicit boundaries.\"]}]}", isLocal: true);
        var service = new StandardAuditService(new CisRepositoryContextResolver(), CreateService(), generation);

        var result = service.Audit(new StandardAuditRequest(repository.Path, Strict: false, DisableLlm: false, Fix: false, Model: null, MaximumPairs: 10));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Findings);
    }

    private static StandardsGovernanceService CreateService()
        => new(new CisRepositoryContextResolver(), new DocumentationCatalogReader());

    private static FixedGraphSnapshotReader PatternGraph(
        string repositoryPath,
        IReadOnlyList<string>? graphCapabilities = null)
    {
        var componentApp = Node("fixture::component::app", "component", "component", "app", "app", null,
            new Dictionary<string, string> { ["languages"] = "csharp", ["roles"] = "shared-library", ["capabilities"] = "commands" });
        var componentTests = Node("fixture::component::tests", "component", "component", "tests", "tests", null,
            new Dictionary<string, string> { ["languages"] = "csharp", ["roles"] = "test-automation", ["capabilities"] = string.Empty });
        var add = Node("fixture::symbol::add", "symbol", "method", "add", "Cis.Abstractions.ICisCommandRegistry.Add(System.CommandLine.Command)", "src/Registry.cs",
            new Dictionary<string, string> { ["component"] = "app", ["qualifiedName"] = "Cis.Abstractions.ICisCommandRegistry.Add(System.CommandLine.Command)" }, ["csharp", "compiler-bound"]);
        var production = Node("fixture::symbol::production", "symbol", "method", "production", "Fixture.Service.Execute()", "src/Service.cs",
            new Dictionary<string, string> { ["component"] = "app", ["qualifiedName"] = "Fixture.Service.Execute()" }, ["csharp", "compiler-bound"]);
        var nodes = new List<CisGraphNode> { componentApp, componentTests, add, production };
        var edges = new List<CisGraphEdge>();
        for (var index = 1; index <= 3; index++)
        {
            var subject = Node($"fixture::symbol::module-{index}", "symbol", "method", $"module-{index}", $"Fixture.Module{index}.RegisterCommands(Cis.Abstractions.ICisCommandRegistry, IServiceProvider)", $"src/Module{index}.cs",
                new Dictionary<string, string> { ["component"] = "app", ["qualifiedName"] = $"Fixture.Module{index}.RegisterCommands(Cis.Abstractions.ICisCommandRegistry, IServiceProvider)" }, ["csharp", "compiler-bound"]);
            nodes.Add(subject); edges.Add(Edge($"module-call-{index}", subject.Key, add.Key));
        }
        nodes.Add(Node("fixture::symbol::generated-module", "symbol", "method", "generated-module", "Fixture.GeneratedModule.RegisterCommands(Cis.Abstractions.ICisCommandRegistry, IServiceProvider)", "src/GeneratedModule.g.cs",
            new Dictionary<string, string> { ["component"] = "app", ["qualifiedName"] = "Fixture.GeneratedModule.RegisterCommands(Cis.Abstractions.ICisCommandRegistry, IServiceProvider)", ["generated"] = "true" }, ["csharp", "compiler-bound", "generated"]));
        for (var index = 1; index <= 5; index++)
        {
            var test = Node($"fixture::test::{index}", "test", "xunit", $"test-{index}", $"FixtureTests.Test{index}", $"src/Fixture.Tests/FixtureTests{index}.cs",
                new Dictionary<string, string> { ["component"] = "tests" }, ["test", "csharp"]);
            nodes.Add(test); edges.Add(Edge($"test-call-{index}", test.Key, production.Key));
        }
        var graph = new CisGraphDocument(1, new("build-1", "fixture", "abc", false, "complete"), nodes, edges, new(0, 0, 0));
        var manifest = new CisGraphManifest(1, "build-1", "fixture", "docs", "abc", false, [], ["cis.csharp.compiler/1"]);
        return new FixedGraphSnapshotReader(repositoryPath, graph, manifest,
            graphCapabilities ?? ["compiler.symbols", "compiler.calls", "tests.symbols", "tests.calls"]);
    }

    private static CisGraphNode Node(
        string key, string kind, string subtype, string localId, string label, string? path,
        IReadOnlyDictionary<string, string> properties, IReadOnlyList<string>? facets = null)
        => new(key, "fixture", kind, subtype, localId, label, facets ?? [], "derived", "active",
            path is null ? [] : [new CisGraphLocation(path, "line", "1")], properties, []);

    private static CisGraphEdge Edge(string key, string from, string to, string type = "calls")
        => new(key, type, from, to, "discovered", "high", [], new Dictionary<string, string>());

    private static string RepositoryPattern(string id) => $$"""
        ---
        title: Custom compiler pattern
        type: standard-inference-pattern
        status: Active
        cis:
          stable_id: {{id}}
        pattern:
          version: 1
          description: Recognize compiler-bound source methods.
          languages: [csharp]
          roles: []
          capabilities: []
          required_graph_capabilities: [compiler.symbols]
          subject:
            kind: symbol
            subtype: method
            facets: [compiler-bound]
            property_equals: {}
            property_contains: {}
            path_prefixes: [src/]
            excluded_path_prefixes: []
          requires: []
          forbids: []
          thresholds:
            minimum_occurrences: 1
            minimum_consistency: 1.0
          candidate:
            target: architecture
            proposed_level: SHOULD
            wording: Compiler-bound source methods SHOULD follow the reviewed repository convention.
            suggested_enforcement: manual-review
            verification: Review matching source methods.
        ---

        # Custom compiler pattern
        """;

    private static byte[] CreateZip(IReadOnlyDictionary<string, string> files)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        foreach (var file in files)
        {
            var entry = archive.CreateEntry(file.Key);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(file.Value);
        }
        return output.ToArray();
    }

    private sealed class ArchiveHandler(byte[] archive) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) });
        }
    }

    private sealed class FixedGenerationService(string response, bool isLocal) : ICisTextGenerationService
    {
        public CisTextGenerationRequest? LastRequest { get; private set; }
        public CisAiStatus GetStatus() => new([new CisAiProviderStatus(isLocal ? "local" : "remote", "available", "test", isLocal, [new CisAiModel("test-model")], null)]);
        public CisTextGenerationResult Generate(CisTextGenerationRequest request)
        {
            LastRequest = request;
            return new("generated", request.Provider, "test-model", response, null, isLocal);
        }
    }

    private sealed class FixedGraphSnapshotReader(
        string repositoryPath,
        CisGraphDocument graph,
        CisGraphManifest manifest,
        IReadOnlyList<string> capabilities) : ICisGraphSnapshotReader
    {
        public CisGraphSnapshotReadResult Read(string path)
            => new("available", 0, repositoryPath, "fresh", graph, manifest, capabilities, [], []);
    }

    private sealed class StandardRepository : IDisposable
    {
        public string Path { get; }
        private StandardRepository(string path) => Path = path;

        public static StandardRepository Create(bool advisory = false)
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cis-standard-tests", Guid.NewGuid().ToString("N"));
            var repository = new StandardRepository(root);
            repository.Write(".cis/repository.yml", "schema_version: 1\nrepository:\n  id: fixture\ndocumentation_root: docs\n");
            repository.Write("docs/catalog.yml", Catalog());
            repository.Write("docs/standards/testing-standard.md", Standard("fixture:standard:testing", includeVerification: true));
            var enforcement = advisory ? "advisory-model" : "deterministic";
            repository.Write("docs/references/standards-conformance-matrix.md", $"""
                # Standards Conformance Matrix
                | Standard ID | Rule ID | Governed surface | Enforcement | Evidence | Status | Notes |
                |---|---|---|---|---|---|---|
                | fixture:standard:testing | TEST-001 | src | {enforcement} | tests/ArchitectureTests.cs | Active | structural |
                | fixture:standard:testing | TEST-002 | tests | manual-review | pull request | Active | proportionate |
                """);
            return repository;
        }

        public void Write(string relative, string content)
        {
            var path = System.IO.Path.Combine(Path, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public static string Catalog(bool includeSecond = false)
        {
            var second = includeSecond
                ? "  - id: fixture:standard:second\n    path: docs/standards/second-standard.md\n    type: standard\n    status: active\n    authority: canonical\n"
                : string.Empty;
            return "schema_version: 1\ndocuments:\n" +
                   "  - id: fixture:standard:testing\n    path: docs/standards/testing-standard.md\n    type: standard\n    status: active\n    authority: canonical\n" +
                   second;
        }

        public static string Standard(string id, bool includeVerification) => $$"""
            ---
            title: Testing Standard
            type: standard
            status: Active
            targets:
              - backend
            stacks:
              - csharp
            owner: Maintainer
            last_reviewed: 2026-08-15
            review_cadence: on change
            source_of_truth: This file
            cis:
              stable_id: {{id}}
            ---
            # Testing Standard
            ## Purpose
            Define expectations.
            ## Scope
            Backend code.
            ## Normative language
            MUST is mandatory.
            ## Rules
            - **TEST-001** Code MUST follow the architecture.
            - **TEST-002** Changes MUST have proportionate tests.
            {{(includeVerification ? "## Verification\nRun the tests." : string.Empty)}}
            ## Exceptions
            Record rationale and approval.
            """;

        public static string ImportedStandard() => """
            ---
            title: Imported Standard
            type: standard
            status: Active
            targets:
              - backend
            stacks:
              - csharp
            owner: Maintainer
            last_reviewed: 2026-08-15
            review_cadence: on change
            source_of_truth: This file
            cis:
              stable_id: fixture:standard:imported
            ---
            # Imported Standard
            ## Purpose
            Govern imports.
            ## Scope
            Backend code.
            ## Normative language
            MUST is mandatory.
            ## Rules
            - **IMP-001** Imports MUST preserve authority.
            ## Verification
            Review the imported rule.
            ## Exceptions
            Record human approval.
            """;

        public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch { } }
    }
}

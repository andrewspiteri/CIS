using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Cis.Host;
using Cis.Modules.Brd;
using Cis.Modules.Docs;
using Cis.Modules.Graph;
using Cis.Modules.Repository;
using Cis.Modules.TechnicalIntent;
using Xunit;

namespace Cis.Modules.TechnicalIntent.Tests;

public sealed class TechnicalIntentWorkflowTests
{
    [Fact]
    public void Commands_AreRegistered()
    {
        using var application = new CisHostBuilder()
            .AddModule(new RepositoryModule())
            .AddModule(new WorkspaceModule())
            .AddModule(new DocsModule())
            .AddModule(new GraphModule())
            .AddModule(new BrdModule())
            .AddModule(new TechnicalIntentModule())
            .Build();

        Assert.Equal(0, application.Invoke(["technical-intent", "init", "--help"]));
        Assert.Equal(0, application.Invoke(["technical-intent", "validate", "--help"]));
        Assert.Equal(0, application.Invoke(["technical-intent", "status", "--help"]));
        Assert.Equal(0, application.Invoke(["technical-intent", "refresh", "--help"]));
        Assert.Equal(0, application.Invoke(["technical-intent", "approve", "--help"]));
    }

    [Fact]
    public void WorkspaceInit_SeedsWorkspaceScopedTechnicalIntent()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: false);

        var content = File.ReadAllText(environment.TechnicalIntentPath);

        Assert.Contains("scope: Workspace", content, StringComparison.Ordinal);
        Assert.Contains("technical_intent_schema: 1", content, StringComparison.Ordinal);
        Assert.Contains("## Open technical decisions", content, StringComparison.Ordinal);
    }

    [Fact]
    public void InactiveBrd_IsAWorkflowBlockRatherThanInvalidTechnicalIntentState()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: false);

        var status = environment.Intent.Status(environment.Authority.Path);
        var initialized = environment.Intent.Initialize(environment.Authority.Path);

        Assert.Equal("status", status.Status);
        Assert.Empty(status.Errors);
        Assert.False(status.Validation!.Current);
        Assert.Contains(status.Validation.Warnings, warning =>
            warning.Contains("Active, current BRD", StringComparison.Ordinal));
        Assert.Equal("blocked", initialized.Status);
        Assert.Equal(5, initialized.ExitCode);
    }

    [Fact]
    public void Lifecycle_RequiresActiveBrdResolvedContentAndExplicitApproval()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true);

        var initialized = environment.Intent.Initialize(environment.Authority.Path);
        Assert.True(initialized.Applied);
        Assert.False(initialized.Validation!.Valid);
        Assert.Contains(initialized.Validation.Errors, error => error.Contains("placeholder", StringComparison.OrdinalIgnoreCase));
        Assert.False(environment.Intent.Evaluate(environment.Authority.Path).Ready);

        CompleteTechnicalReview(environment.TechnicalIntentPath);
        var ready = environment.Intent.Validate(environment.Authority.Path);
        Assert.True(ready.Validation!.Valid);
        Assert.True(ready.Validation.Current);
        Assert.Equal("Ready for Approval", ready.Validation.EffectiveStatus);
        var pendingApproval = environment.Intent.Evaluate(environment.Authority.Path);
        Assert.False(pendingApproval.Ready);
        Assert.DoesNotContain(pendingApproval.Errors, error =>
            error.Contains("Authority repository graph is stale", StringComparison.Ordinal));

        var approved = environment.Intent.Approve(
            environment.Authority.Path,
            "Architecture owner",
            "The reviewed workspace technical direction is accepted.");
        Assert.True(approved.Applied);
        Assert.Equal("Active", approved.Validation!.EffectiveStatus);
        Assert.True(environment.Intent.Evaluate(environment.Authority.Path).Ready);

        File.AppendAllText(environment.TechnicalIntentPath, "\nMaterial unapproved direction.\n");
        var stale = environment.Intent.Status(environment.Authority.Path);
        Assert.Equal("Stale", stale.Validation!.EffectiveStatus);
        Assert.False(stale.Validation.Current);
        Assert.False(environment.Intent.Evaluate(environment.Authority.Path).Ready);
    }

    [Fact]
    public void ApprovedIntent_RemainsActiveForBrdProvenanceAndMigratesLegacyBaselineWithoutReapproval()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true);
        Assert.Equal(0, environment.Intent.Initialize(environment.Authority.Path).ExitCode);
        CompleteTechnicalReview(environment.TechnicalIntentPath);
        Assert.Equal(0, environment.Intent.Approve(environment.Authority.Path, "Architecture owner",
            "The reviewed workspace technical direction is accepted.").ExitCode);

        var brdPath = Path.Combine(environment.Authority.Path, "docs", "specs", "business-requirements.md");
        var brd = File.ReadAllText(brdPath).Replace(
            "<!-- cis:feature-traceability:end -->",
            "<!-- provenance-only reconciliation -->\n<!-- cis:feature-traceability:end -->",
            StringComparison.Ordinal);
        File.WriteAllText(brdPath, brd);

        var provenanceOnly = environment.Intent.Status(environment.Authority.Path);
        Assert.Equal("Active", provenanceOnly.Validation!.EffectiveStatus);
        Assert.True(provenanceOnly.Validation.Current);

        var intent = File.ReadAllText(environment.TechnicalIntentPath);
        var legacyHash = "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(brd))).ToLowerInvariant();
        intent = Regex.Replace(intent, @"semantic-v1:sha256:[a-f0-9]{64}", legacyHash,
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        File.WriteAllText(environment.TechnicalIntentPath, intent);
        var legacy = environment.Intent.Status(environment.Authority.Path);
        Assert.Equal("Stale", legacy.Validation!.EffectiveStatus);
        Assert.DoesNotContain(legacy.Validation.Warnings, warning =>
            warning.Contains("content changed after approval", StringComparison.OrdinalIgnoreCase));

        var migrated = environment.Intent.Initialize(environment.Authority.Path);
        Assert.True(migrated.Applied);
        Assert.Equal("Active", migrated.Validation!.EffectiveStatus);
        var migratedContent = File.ReadAllText(environment.TechnicalIntentPath);
        Assert.Contains("semantic-v1:sha256:", migratedContent, StringComparison.Ordinal);
        Assert.Contains("approved_by: \"Architecture owner\"", migratedContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Initialize_PreservesApprovalWhenOnlyRepositoryBaselineVersionChanges()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true);
        Assert.Equal(0, environment.Intent.Initialize(environment.Authority.Path).ExitCode);
        CompleteTechnicalReview(environment.TechnicalIntentPath);
        Assert.Equal(0, environment.Intent.Approve(
            environment.Authority.Path,
            "Architecture owner",
            "The reviewed workspace technical direction is accepted.").ExitCode);
        var content = File.ReadAllText(environment.TechnicalIntentPath);
        content = Regex.Replace(
            content,
            @"(?m)^(\| repository \| [^|]+ \| )sha256:[a-f0-9]{64}( \|)",
            "$1sha256:0000000000000000000000000000000000000000000000000000000000000000$2",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
        File.WriteAllText(environment.TechnicalIntentPath, content);

        var stale = environment.Intent.Status(environment.Authority.Path);
        Assert.Equal("Stale", stale.Validation!.EffectiveStatus);

        var reconciled = environment.Intent.Initialize(environment.Authority.Path);

        Assert.Equal(0, reconciled.ExitCode);
        Assert.True(reconciled.Applied);
        Assert.Equal("Active", reconciled.Validation!.EffectiveStatus);
        Assert.True(reconciled.Validation.Current);
        var reconciledContent = File.ReadAllText(environment.TechnicalIntentPath);
        Assert.Contains("approved_by: \"Architecture owner\"", reconciledContent, StringComparison.Ordinal);
        Assert.Contains("approval_reason: \"The reviewed workspace technical direction is accepted.\"",
            reconciledContent, StringComparison.Ordinal);
    }

    [Fact]
    public void GovernanceRefresh_PreservesAuthorityAcrossImplementationOnlyGraphDrift()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true);
        Assert.Equal(0, environment.Intent.Initialize(environment.Authority.Path).ExitCode);
        CompleteTechnicalReview(environment.TechnicalIntentPath);
        Assert.Equal(0, environment.Intent.Approve(environment.Authority.Path, "Architecture owner",
            "The reviewed workspace technical direction is accepted.").ExitCode);
        var backlog = environment.CreateBacklog();
        Assert.Equal("built", backlog.Build(environment.Authority.Path).Status);
        Assert.Equal("approved", backlog.Approve(environment.Authority.Path, "Product owner",
            "The reviewed high-level outcomes are accepted.").Status);

        environment.Participant.Write("src/Product/Product.cs", "public sealed class Product { public string Name => \"changed implementation\"; }");
        environment.RebuildParticipantGraph();
        Assert.Equal("Stale", environment.Brd.Status(environment.Authority.Path).Validation!.EffectiveStatus);
        Assert.Equal("Stale", environment.Intent.Status(environment.Authority.Path).Validation!.EffectiveStatus);

        var refreshed = new GovernanceRefreshService(environment.Brd, environment.Intent, backlog)
            .Refresh(environment.Authority.Path);

        Assert.Equal(0, refreshed.ExitCode);
        Assert.Equal("refreshed", refreshed.Status);
        Assert.Equal("Active", refreshed.Brd.Validation!.EffectiveStatus);
        Assert.Equal("Active", refreshed.TechnicalIntent!.Validation!.EffectiveStatus);
        Assert.Equal("Active", refreshed.Backlog!.Validation!.EffectiveStatus);
        Assert.Contains("approved_by: \"Product owner\"",
            File.ReadAllText(Path.Combine(environment.Authority.Path, "docs", "plans", "high-level-backlog.md")),
            StringComparison.Ordinal);
    }

    private static void CompleteTechnicalReview(string path)
    {
        var content = File.ReadAllText(path);
        if (!content.Contains("## Approved business baseline", StringComparison.Ordinal))
        {
            content = content.Replace(
                "## Design goals and principles",
                "## Approved business baseline\n\nThe active BRD scope and exclusions are preserved.\n\n## Design goals and principles",
                StringComparison.Ordinal);
        }
        content = Regex.Replace(content, "TODO:[^\r\n]*", "Reviewed technical direction.",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        content = content.Replace("| TODO | TODO | TODO | TODO |", "| workspace | `.` | markdown | authority |", StringComparison.Ordinal);
        content = content.Replace("| Reliability | TODO | TODO |", "| Reliability | Deterministic behavior | Integration tests |", StringComparison.Ordinal);
        content = content.Replace("| Performance | TODO | TODO |", "| Performance | Bounded latency | Load test |", StringComparison.Ordinal);
        content = content.Replace("| Maintainability | TODO | TODO |", "| Maintainability | Explicit boundaries | Architecture tests |", StringComparison.Ordinal);
        content = content.Replace("| Accessibility | TODO | TODO |", "| Accessibility | WCAG intent | Accessibility review |", StringComparison.Ordinal);
        content = content.Replace("| Security | TODO | TODO |", "| Security | Default deny | Authorization tests |", StringComparison.Ordinal);
        content = content.Replace("| TI-DEC-001 | TODO | Change dossier creation | Open | TODO |",
            "| TI-DEC-001 | Contract ownership | Change dossier creation | Resolved | OpenAPI is canonical and drift-tested. |", StringComparison.Ordinal);
        File.WriteAllText(path, content);
    }

    private sealed class WorkspaceEnvironment : IDisposable
    {
        private WorkspaceEnvironment(TemporaryRepository authority, TemporaryRepository participant,
            TechnicalIntentService intent, BrdService brd, WorkspaceRegistry registry,
            CisRepositoryContextResolver resolver, DocumentationCatalogMerger merger)
        {
            Authority = authority;
            Participant = participant;
            Intent = intent;
            Brd = brd;
            Registry = registry;
            Resolver = resolver;
            Merger = merger;
        }

        public TemporaryRepository Authority { get; }
        public TemporaryRepository Participant { get; }
        public TechnicalIntentService Intent { get; }
        public BrdService Brd { get; }
        public WorkspaceRegistry Registry { get; }
        public CisRepositoryContextResolver Resolver { get; }
        public DocumentationCatalogMerger Merger { get; }
        public string TechnicalIntentPath => Path.Combine(Authority.Path, "docs", "specs", "technical-intent-spec.md");

        public static WorkspaceEnvironment Create(bool approveBrd)
        {
            var authority = TemporaryRepository.Create();
            var participant = TemporaryRepository.Create();
            participant.Write("src/Product/Product.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            participant.Write("src/Product/Product.cs", "public sealed class Product { }");
            var resolver = new CisRepositoryContextResolver();
            var registry = new WorkspaceRegistry(resolver);
            var workspaceInit = new WorkspaceInitializer(new RepositoryInitializer(), registry).Initialize(
                new WorkspaceInitRequest(authority.Path, "docs", false, true));
            Assert.Equal(0, workspaceInit.ExitCode);
            var imported = new RepositoryImporter(new RepositoryInitializer(), registry).Import(
                new RepositoryImportRequest(authority.Path, "docs/cis", [participant.Path], false, true));
            Assert.Equal(0, imported.ExitCode);
            var builder = CreateBuilder(resolver);
            Assert.Equal(0, builder.Build(authority.Path).ExitCode);
            Assert.Equal(0, builder.Build(participant.Path).ExitCode);
            var graphReader = new GraphSnapshotReader(resolver);
            var merger = new DocumentationCatalogMerger();
            var brd = new BrdService(registry, resolver, new GraphValidator(resolver), graphReader, merger,
                () => new DateTimeOffset(2026, 8, 16, 10, 0, 0, TimeSpan.Zero));
            if (approveBrd)
            {
                Assert.Equal(0, brd.Initialize(authority.Path, "Product BRD").ExitCode);
                var brdPath = Path.Combine(authority.Path, "docs", "specs", "business-requirements.md");
                var content = File.ReadAllText(brdPath);
                content = Regex.Replace(content, "(?m)^TODO: Complete .+$",
                    "Stakeholders recorded complete and testable requirements.",
                    RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                content = Regex.Replace(content,
                    "(?ms)^## Functional requirements\\s*$.*?(?=^## )",
                    "## Functional requirements\n\n| ID | Requirement | Priority | Acceptance intent |\n| --- | --- | --- | --- |\n| BRD-FR-001 | A user shall view the product. | Must | The reviewed product view is available. |\n\n",
                    RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                File.WriteAllText(brdPath, content);
                Assert.Equal(0, brd.Approve(authority.Path, "Product owner", "Requirements accepted.").ExitCode);
            }
            var intent = new TechnicalIntentService(registry, resolver, graphReader, brd, merger,
                () => new DateTimeOffset(2026, 8, 16, 11, 0, 0, TimeSpan.Zero));
            return new WorkspaceEnvironment(authority, participant, intent, brd, registry, resolver, merger);
        }

        public BrdBacklogService CreateBacklog()
            => new(Brd, Registry, Resolver, Merger, [Intent],
                () => new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero));

        public void RebuildParticipantGraph()
            => Assert.Equal(0, CreateBuilder(Resolver).Build(Participant.Path).ExitCode);

        public void Dispose()
        {
            Participant.Dispose();
            Authority.Dispose();
        }

        private static GraphBuilder CreateBuilder(CisRepositoryContextResolver resolver)
        {
            var reader = new DocumentationCatalogReader();
            var inspector = new MarkdownDocumentInspector();
            var inventory = new DocumentationInventoryService(resolver, reader, inspector);
            var validation = new DocumentationValidationService(resolver, reader, inventory, inspector);
            return new GraphBuilder(resolver, reader, inventory, validation);
        }
    }

    private sealed class TemporaryRepository : IDisposable
    {
        private TemporaryRepository(string path) => Path = path;
        public string Path { get; }

        public static TemporaryRepository Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cis-technical-intent-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TemporaryRepository(path);
        }

        public void Write(string relativePath, string content)
        {
            var path = System.IO.Path.Combine(Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public void Dispose()
        {
            var path = System.IO.Path.GetFullPath(Path);
            var parent = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cis-technical-intent-tests")) + System.IO.Path.DirectorySeparatorChar;
            if (path.StartsWith(parent, StringComparison.OrdinalIgnoreCase)) Directory.Delete(path, true);
        }
    }
}

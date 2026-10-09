using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;
using Cis.Host;

namespace Cis.Modules.Brd.Tests;

public sealed partial class BrdWorkflowTests
{
    [Fact]
    public void ReconcileFeature_PreservesBodyClearsApprovalAndRetainsRecoveryEvidence()
    {
        using var fixture = FeatureReconciliationFixture.Create();
        var before = File.ReadAllText(fixture.FeaturePath);

        var result = fixture.Reconcile();

        Assert.True(result.ExitCode == 0, string.Join("; ", result.Errors.Concat(result.Validation?.Errors ?? [])));
        Assert.Equal("reconciled", result.Status);
        Assert.Equal("Review Required", result.Validation!.DocumentStatus);
        var after = File.ReadAllText(fixture.FeaturePath);
        Assert.Equal(Regex.Replace(before, @"\A---\r?\n.*?\r?\n---", "", RegexOptions.Singleline),
            Regex.Replace(after, @"\A---\r?\n.*?\r?\n---", "", RegexOptions.Singleline));
        Assert.Equal("null", CisFrontMatter.Read(after, "approved_content_hash", nested: true));
        Assert.Equal(fixture.Provider.BaselineHash, CisFrontMatter.Read(after, "product_definition_hash", nested: true));
        Assert.False(new BrdFeatureApprovalAuthority(fixture.Service).Evaluate(fixture.Root, fixture.FeatureRelative).Ready);
        var backup = Assert.Single(Directory.GetDirectories(Path.Combine(fixture.Root, ".cis/local/feature-reconciliation")));
        Assert.Equal(Encoding.UTF8.GetBytes(before), File.ReadAllBytes(Path.Combine(backup, "0.original")));
        Assert.Contains("\"approvalGranted\":false", File.ReadAllText(Path.Combine(backup, "record.json")));
        fixture.RenewParentEvidence();
        Assert.Equal("approved", fixture.Service.ApproveFeature(fixture.Root, "HLT-FR-001", "Test owner", "Reviewed documentary compatibility").Status);
    }

    [Theory]
    [InlineData("feature-drift")]
    [InlineData("definition-drift")]
    [InlineData("prior-baseline")]
    [InlineData("current-artifact")]
    [InlineData("prior-artifact")]
    [InlineData("missing-prior")]
    [InlineData("missing-artifact")]
    [InlineData("duplicate-artifact")]
    [InlineData("null-artifact")]
    [InlineData("incompatible")]
    [InlineData("unresolved")]
    [InlineData("false-unchanged")]
    [InlineData("current-as-prior")]
    [InlineData("backslash-alias")]
    [InlineData("comparison-is-feature")]
    [InlineData("prior-is-catalogue")]
    [InlineData("prior-is-feature")]
    [InlineData("path-traversal")]
    [InlineData("comparison-drift")]
    [InlineData("inactive")]
    [InlineData("no-inventory")]
    [InlineData("malformed")]
    [InlineData("schema")]
    [InlineData("edited-feature")]
    [InlineData("backlog-scope")]
    public void ReconcileFeature_RejectsUnreviewedOrIncompleteEvidenceWithoutCanonicalWrites(string fault)
    {
        using var fixture = FeatureReconciliationFixture.Create();
        var review = fixture.Review;
        var first = review.Artifacts[0];
        switch (fault)
        {
            case "feature-drift": File.AppendAllText(fixture.FeaturePath, "\nUnreviewed note\n"); break;
            case "definition-drift": fixture.Provider.BaselineHash = FeatureReconciliationFixture.Digest("unexpected"); break;
            case "prior-baseline": review = review with { PreviousProductDefinitionHash = "wrong" }; break;
            case "current-artifact": File.AppendAllText(Path.Combine(fixture.Root, first.Path), "changed"); break;
            case "prior-artifact": File.AppendAllText(Path.Combine(fixture.Root, first.Prior.Path), "changed"); break;
            case "missing-prior": File.Delete(Path.Combine(fixture.Root, first.Prior.Path)); break;
            case "missing-artifact": review = review with { Artifacts = review.Artifacts.Skip(1).ToArray() }; break;
            case "duplicate-artifact": review = review with { Artifacts = [first, first] }; break;
            case "null-artifact": review = review with { Artifacts = [null!] }; break;
            case "incompatible": first = first with { Assessment = "incompatible" }; break;
            case "unresolved": first = first with { Reason = "" }; break;
            case "false-unchanged": first = first with { Assessment = "unchanged" }; break;
            case "current-as-prior": first = first with { Prior = new(first.Path, first.CurrentSha256) }; break;
            case "backslash-alias": first = first with { Prior = new(first.Path.Replace('/', '\\'), first.CurrentSha256) }; break;
            case "comparison-is-feature": review = review with { Comparison = new(fixture.FeatureRelative, review.ExpectedFeatureSha256) }; break;
            case "prior-is-feature": first = first with { Prior = new(fixture.FeatureRelative, review.ExpectedFeatureSha256) }; break;
            case "prior-is-catalogue": first = first with { Prior = new("docs/catalog.yml", FeatureReconciliationFixture.Digest(File.ReadAllBytes(fixture.CatalogPath))) }; break;
            case "path-traversal": first = first with { Prior = first.Prior with { Path = "../outside.md" } }; break;
            case "comparison-drift": File.AppendAllText(Path.Combine(fixture.Root, review.Comparison.Path), "changed"); break;
            case "inactive": fixture.Provider.Active = false; break;
            case "no-inventory": fixture.Provider.Paths = []; break;
            case "schema": review = review with { SchemaVersion = 99 }; break;
            case "backlog-scope": fixture.ChangeBacklogScope(); break;
            case "edited-feature":
                File.AppendAllText(fixture.FeaturePath, "\nNew scope\n");
                review = review with { ExpectedFeatureSha256 = FeatureReconciliationFixture.Digest(File.ReadAllBytes(fixture.FeaturePath)) }; break;
        }
        if (first != fixture.Review.Artifacts[0]) review = review with { Artifacts = [first, review.Artifacts[1]] };
        fixture.WriteReview(review);
        if (fault == "malformed") File.WriteAllText(Path.Combine(fixture.Root, fixture.ReviewPath), "{ broken");
        var originals = new[] { fixture.FeaturePath, fixture.CatalogPath }.ToDictionary(path => path, File.ReadAllBytes);

        var result = fixture.Reconcile();

        Assert.Equal("blocked", result.Status);
        Assert.Equal(5, result.ExitCode);
        Assert.NotEmpty(result.Errors);
        foreach (var (path, bytes) in originals) Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReconcileFeature_RestoresBothFilesWhenPostWriteAuthorityChangesOrThrows(bool throws)
    {
        using var fixture = FeatureReconciliationFixture.Create();
        var originals = new[] { fixture.FeaturePath, fixture.CatalogPath }.ToDictionary(path => path, File.ReadAllBytes);
        fixture.Provider.BeforeEvaluate = () =>
        {
            if (CisFrontMatter.Read(File.ReadAllText(fixture.FeaturePath), "status") != "Review Required") return;
            if (throws) throw new InvalidOperationException("Injected authority failure after write");
            fixture.Provider.Active = false;
        };

        var result = fixture.Reconcile();

        Assert.Equal("blocked", result.Status);
        Assert.Contains(result.Errors, error => error.Contains("restored", StringComparison.Ordinal));
        foreach (var (path, bytes) in originals) Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void ReconcileFeature_AlreadyCurrentDoesNotWriteOrChangeApproval()
    {
        using var fixture = FeatureReconciliationFixture.Create();
        fixture.Provider.BaselineHash = fixture.Review.PreviousProductDefinitionHash;
        fixture.WriteReview(fixture.Review with { ExpectedProductDefinitionHash = fixture.Provider.BaselineHash });
        var before = File.ReadAllBytes(fixture.FeaturePath);
        var result = fixture.Reconcile();
        Assert.Equal("unchanged", result.Status);
        Assert.Equal("Active", result.Validation!.EffectiveStatus);
        Assert.Equal(before, File.ReadAllBytes(fixture.FeaturePath));
    }

    [Theory]
    [InlineData("json")]
    [InlineData("agent")]
    [InlineData("human")]
    public void ReconcileFeatureCommand_DispatchesAndPropagatesBlockedExitCode(string format)
    {
        using var fixture = FeatureReconciliationFixture.Create();
        using var app = new CisHostBuilder().AddModule(new RepositoryModule()).AddModule(new WorkspaceModule())
            .AddModule(new DocsModule()).AddModule(new GraphModule()).AddModule(new BrdModule()).Build();
        Assert.Equal(5, app.Invoke(["brd", "feature", "reconcile", "--workspace", fixture.Root,
            "--item", "HLT-FR-001", "--review", fixture.ReviewPath, "--actor", "Test reviewer", "--reason", "Test comparison", "--format", format]));
    }

    [Theory]
    [InlineData("[]", false)]
    [InlineData("null", false)]
    [InlineData("{ broken", false)]
    [InlineData("[]", true)]
    [InlineData("null", true)]
    public void ReconcileFeatureCommand_MalformedAuthorityInputsReturnBlockedJson(string malformed, bool session)
    {
        using var fixture = FeatureReconciliationFixture.Create();
        var input = Path.Combine(fixture.Root, session ? ".cis/local/definition-wizard/session.json" : CisProductDocumentPaths.SelectionFile);
        Directory.CreateDirectory(Path.GetDirectoryName(input)!);
        File.WriteAllText(input, malformed);
        using var app = new CisHostBuilder().AddModule(new RepositoryModule()).AddModule(new WorkspaceModule())
            .AddModule(new DocsModule()).AddModule(new GraphModule()).AddModule(new BrdModule())
            .AddModule(new Cis.Modules.TechnicalIntent.TechnicalIntentModule())
            .AddModule(new Cis.Modules.SolutionDesign.SolutionDesignModule())
            .AddModule(new Cis.Modules.UiDirection.UiDirectionModule())
            .AddModule(new Cis.Modules.Definition.DefinitionModule()).Build();
        var originalOut = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            Assert.Equal(5, app.Invoke(["brd", "feature", "reconcile", "--workspace", fixture.Root,
                "--item", "HLT-FR-001", "--review", fixture.ReviewPath, "--actor", "Test reviewer",
                "--reason", "Test comparison", "--format", "json"]));
        }
        finally { Console.SetOut(originalOut); }
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal("blocked", json.RootElement.GetProperty("status").GetString());
        Assert.NotEmpty(json.RootElement.GetProperty("errors").EnumerateArray());
    }

    private sealed class ReconciliationDefinition : ICisProductDefinitionAuthority
    {
        public bool Active { get; set; } = true;
        public string BaselineHash { get; set; } = FeatureReconciliationFixture.Digest("old definition");
        public IReadOnlyList<string> Paths { get; set; } = [];
        public Action? BeforeEvaluate { get; set; }
        public CisProductDefinitionAuthority Evaluate(string repositoryPath)
        {
            BeforeEvaluate?.Invoke();
            return new(true, Active, "TEST-DEFINITION", "2026-10-08T00:00:00Z", BaselineHash, []);
        }
        public IReadOnlyList<string> EvidencePaths(string repositoryPath) => Paths;
    }

    private sealed class FeatureReconciliationFixture(WorkspaceEnvironment environment, BrdBacklogService service,
        ReconciliationDefinition provider, string relative, CisFeatureReconciliationReview review) : IDisposable
    {
        public string Root => environment.Authority.Path;
        public BrdBacklogService Service => service;
        public ReconciliationDefinition Provider => provider;
        public string FeatureRelative => relative;
        public string FeaturePath => Path.Combine(Root, relative);
        public string CatalogPath => Path.Combine(Root, "docs/catalog.yml");
        public string ReviewPath => ".cis/local/compatibility/review.json";
        public CisFeatureReconciliationReview Review => review;
        public BrdFeatureResult Reconcile() => service.ReconcileFeature(Root, "HLT-FR-001", ReviewPath, "Test reviewer", "Documentary corrections preserve feature scope");
        public void WriteReview(CisFeatureReconciliationReview value) => File.WriteAllText(Path.Combine(Root, ReviewPath),
            JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        public static string Digest(string value) => Digest(Encoding.UTF8.GetBytes(value));
        public static string Digest(byte[] value) => "sha256:" + Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
        public void ChangeBacklogScope()
        {
            var brd = File.ReadAllText(environment.CanonicalPath);
            Assert.Contains("Record a reading.", brd);
            File.WriteAllText(environment.CanonicalPath, brd.Replace("Record a reading.", "Record an amended reading.", StringComparison.Ordinal));
            Assert.Equal(0, environment.Service.Approve(Root, "Test owner", "Changed synthetic business scope").ExitCode);
            Assert.Equal(0, service.Build(Root, null, "Test owner", applyReviewedChanges: true).ExitCode);
            var approved = service.Approve(Root, "Test owner", "Changed synthetic backlog scope");
            Assert.True(approved.ExitCode == 0, string.Join("; ", approved.Errors.Concat(approved.Validation?.Errors ?? [])));
        }

        public void RenewParentEvidence()
        {
            Assert.Equal(0, environment.Service.Reconcile(Root).ExitCode);
            CompleteHumanReview(environment.CanonicalPath, true);
            Assert.Equal(0, environment.Service.Approve(Root, "Test owner", "Reviewed feature source").ExitCode);
            Assert.Equal(0, service.Build(Root, null, "Test owner", applyReviewedChanges: true).ExitCode);
            Assert.Equal(0, service.Approve(Root, "Test owner", "Unchanged item with refreshed upstream evidence").ExitCode);
            Assert.Equal("Active", service.Status(Root).Validation!.EffectiveStatus);
        }
        public void Dispose() => environment.Dispose();

        public static FeatureReconciliationFixture Create()
        {
            var environment = WorkspaceEnvironment.Create(1);
            Assert.Equal(0, environment.Service.Initialize(environment.Authority.Path, "Reconciliation fixture").ExitCode);
            CompleteHumanReview(environment.CanonicalPath, false);
            var brd = Regex.Replace(File.ReadAllText(environment.CanonicalPath),
                @"(?ms)^## Functional requirements\s*$.*?(?=^## )",
                "## Functional requirements\n\n| ID | Requirement | Priority | Acceptance intent |\n| --- | --- | --- | --- |\n| BRD-FR-001 | Record a reading. | Must | The reading can be retrieved. |\n\n");
            File.WriteAllText(environment.CanonicalPath, brd);
            Assert.Equal(0, environment.Service.Approve(environment.Authority.Path, "Test owner", "Synthetic scope").ExitCode);
            var provider = new ReconciliationDefinition();
            var service = new BrdBacklogService(environment.Service, environment.Registry, new CisRepositoryContextResolver(),
                new DocumentationCatalogMerger(), [new ReadyCheck()], productDefinitionAuthorities: [provider]);
            Assert.Equal("built", service.Build(environment.Authority.Path).Status);
            Assert.Equal("approved", service.Approve(environment.Authority.Path, "Test owner", "Synthetic scope").Status);
            var started = service.StartFeature(environment.Authority.Path, "HLT-FR-001");
            var featurePath = Path.Combine(environment.Authority.Path, started.RelativePath!);
            File.WriteAllText(featurePath, File.ReadAllText(featurePath).Replace("TODO", "Reviewed", StringComparison.Ordinal));
            Assert.Equal("approved", service.ApproveFeature(environment.Authority.Path, "HLT-FR-001", "Test owner", "Synthetic scope").Status);
            Assert.Equal(0, environment.Service.Reconcile(environment.Authority.Path).ExitCode);
            CompleteHumanReview(environment.CanonicalPath, true);
            Assert.Equal(0, environment.Service.Approve(environment.Authority.Path, "Test owner", "Reviewed feature source").ExitCode);
            Assert.Equal(0, service.Build(environment.Authority.Path, null, "Test owner", applyReviewedChanges: true).ExitCode);
            Assert.Equal(0, service.Approve(environment.Authority.Path, "Test owner", "Unchanged item with refreshed upstream evidence").ExitCode);
            var priorHash = provider.BaselineHash;
            provider.BaselineHash = Digest("new definition");
            provider.Paths = ["docs/compatibility-direction.md", "docs/compatibility-contracts.md"];
            var artifacts = provider.Paths.Select((path, index) =>
            {
                var prior = $".cis/local/compatibility/prior-{index}.md";
                var oldText = index == 0 ? "Record one reading using the console.\n" : "Reading contract v1\n";
                var newText = index == 0 ? "Record one reading using the command-line interface.\n" : oldText;
                environment.Authority.Write(prior, oldText);
                environment.Authority.Write(path, newText);
                return new CisFeatureReconciliationComparison(path, Digest(newText), new(prior, Digest(oldText)),
                    index == 0 ? "compatible" : "unchanged", index == 0 ? "Terminology only; same single reading and interface." : "Identical contract bytes.");
            }).ToArray();
            const string comparison = ".cis/local/compatibility/comparison.md";
            const string comparisonText = "The direction comparison changes console to command-line interface; one-reading scope and contracts remain unchanged.\n";
            environment.Authority.Write(comparison, comparisonText);
            var review = new CisFeatureReconciliationReview(1, Digest(File.ReadAllBytes(featurePath)), priorHash,
                provider.BaselineHash, new(comparison, Digest(comparisonText)), artifacts);
            var fixture = new FeatureReconciliationFixture(environment, service, provider, started.RelativePath!, review);
            fixture.WriteReview(review);
            return fixture;
        }
    }
}
